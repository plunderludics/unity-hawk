using UnityEngine;
using NaughtyAttributes;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Collections.Concurrent;

namespace UnityHawk {

public partial class Emulator {
    [Foldout("Debug")]
    [ShowIf("captureEmulatorAudio")]
    [Tooltip("Higher value means more audio latency. Lower value may cause crackles and pops")]
    public int audioBufferSurplus = (int)(44100*0.05);

    [Foldout("Debug")]
    [ShowIf("captureEmulatorAudio")]
    [ReadOnly, SerializeField]
    private double _avgSamplesProvided;

    [Foldout("Debug")]
    [ShowIf("captureEmulatorAudio")]
    [ReadOnly, SerializeField]
    private int rawBufferCount;

    static int RawBufferSize = (int)(2*44100*1); // Size of local audio buffer, 1 sec should be plenty    
    private ConcurrentQueue<short> _rawBuffer;

    [Foldout("Debug")]
    [ShowIf("captureEmulatorAudio")]
    public int movingAverageN = 5;
    private List<int> _samplesProvidedHistory;

    [Foldout("Debug")]
    [ShowIf("captureEmulatorAudio")]
    public float bufferPressure = 0.01f;
    
    [Foldout("Debug")]
    [ShowIf("captureEmulatorAudio")]
    public bool forceRatio;
    
    [Foldout("Debug")]
    [ShowIf(EConditionOperator.And, "captureEmulatorAudio", "forceRatio")]
    public float forcedRatio = 1f;

    

    // Track how many times we skip audio, log a warning if it's too much
    float _audioSkipCounter;
    float _acceptableSkipsPerSecond = 1f;

    private int samplesProvidedThisFrame;

    private const int ChannelCount = 2;

    [Foldout("Debug")]
    [ShowIf("captureEmulatorAudio")]
    [ReadOnly, SerializeField]
    private double resampleRatio = 1f;

    [Button]
    public void OnVolatility() {
        // Reset everything
        _rawBuffer = new();
        _samplesProvidedHistory = new();
    }

    void InitAudio() {
        // Init local audio buffer
        _rawBuffer = new();
        
        _audioSkipCounter = 0f;
        samplesProvidedThisFrame = 0;

        _samplesProvidedHistory = new();
    }
    void UpdateAudio() {
        CaptureBizhawkAudio(); // Probably don't need to do this every frame, but if it's fast enough it's fine

        rawBufferCount = _rawBuffer.Count;

        _audioSkipCounter += _acceptableSkipsPerSecond*Time.deltaTime;
        if (_audioSkipCounter < 0f) {
            if (Time.realtimeSinceStartup > 5f) { // ignore the first few seconds while bizhawk is starting up
                Debug.LogWarning("Suffering frequent audio drops (consider increasing audioBufferSurplus value)");
            }
            _audioSkipCounter = 0f;
        }
    }
    void CaptureBizhawkAudio() {
        short[] samples = _sharedAudioBuffer.GetSamples();
        if (samples == null) return; // This is fine, sometimes bizhawk just doesn't have any samples ready

        samplesProvidedThisFrame += samples.Length/ChannelCount;
        // Append samples to running audio buffer to be played back later
        // [Doing an Array.Copy here instead would probably be way faster but not a big deal]
        for (int i = 0; i < samples.Length; i++) {
            // TODO may want to cap the size of the queue
            _rawBuffer.Enqueue(samples[i]);
        }
    }

    // Send audio from the emulator to the AudioSource
    // (this method gets called by Unity if there is an AudioSource component attached)
    void OnAudioFilterRead(float[] out_buffer, int channels) {
        if (!captureEmulatorAudio) return;
        if (!_sharedAudioBuffer.IsOpen()) return;
        if (channels != 2) {
            Debug.LogError("AudioSource must be set to 2 channels");
            return;
        }

        // Resample
        int stereoSamplesNeeded = out_buffer.Length/ChannelCount;

        _samplesProvidedHistory.Add(samplesProvidedThisFrame);
        samplesProvidedThisFrame = 0;
        while (_samplesProvidedHistory.Count > movingAverageN) {
            _samplesProvidedHistory.RemoveAt(0);
        }
        double avgSamplesProvided = _samplesProvidedHistory.Sum()/(double)_samplesProvidedHistory.Count; // TODO maybe should be weighted average or something here

        _avgSamplesProvided = avgSamplesProvided;

        // Calculate rescale ratio, add to history, then calculate smoothed ratio based on moving average
        double ratio = (double)avgSamplesProvided/stereoSamplesNeeded;
        resampleRatio = ratio;

        // Experimental: increase ratio slightly when there are too many samples in the buffer, to stop it getting too long
        int excessStereoSamples = (_rawBuffer.Count - audioBufferSurplus)/ChannelCount;
        if (excessStereoSamples > 0) {
            double excessRatio = excessStereoSamples/(double)stereoSamplesNeeded;
            ratio = ratio*(1-bufferPressure) + excessRatio*bufferPressure;
        }

        if (forceRatio) ratio = forcedRatio;

        int stereoSamplesToConsume = (int)(ratio*stereoSamplesNeeded);
        int availableStereoSamples = _rawBuffer.Count/ChannelCount;

        // Debug.Log($"Want {stereoSamplesToConsume} samples, {availableStereoSamples} are available");
        if (stereoSamplesToConsume > availableStereoSamples) {
            Debug.LogWarning($"Starved of bizhawk samples");
            _audioSkipCounter -= 1f;
            stereoSamplesToConsume = availableStereoSamples;
        }

        // Pop `stereoSamplesToConsume` samples off the buffer
        short[] rawSamples = new short[stereoSamplesToConsume*ChannelCount]; // TODO init elsewhere
        for (int i = 0; i < rawSamples.Length; i++) {
            short x;
            _rawBuffer.TryDequeue(out x);
            rawSamples[i] = x;
        }
        Debug.Log($"Resampling from {stereoSamplesToConsume} to {stereoSamplesNeeded} ({ratio})");
        short[] resampled = Resample(rawSamples, stereoSamplesToConsume, stereoSamplesNeeded);

        // copy from the local running audio buffer into unity's buffer, convert short to float
        int out_i;
        for (out_i = 0; out_i < out_buffer.Length; out_i++) {
            if (out_i < resampled.Length) {
                out_buffer[out_i] = resampled[out_i]/32767f;
            } else {
                Debug.LogError("Ran out of resampled audio, this should never happen");
                break;
            }
        }

        // Clear buffer except for a small amount of samples leftover (as buffer against skips/pops)
        // (kind of a dumb way of doing this, could just reset _audioBufferEnd but whatever)
        // int droppedSamples = 0;
        // while (_resampledBuffer.Count > audioBufferSurplus*ChannelCount) {
        //     _ = _resampledBuffer.Dequeue();
        //     droppedSamples++;
        // }
        // if (droppedSamples > 0) Debug.LogWarning($"Dropped {droppedSamples} samples from bizhawk");
    }

    // Simple linear interpolation, based on SoundOutputProvider.cs in Bizhawk
    private short[] Resample(short[] input, int inputCount, int outputCount)
    {
        if (inputCount == outputCount)
        {
            return input;
        }

        short[] output = new short[outputCount*ChannelCount]; // Not efficient to initialize every frame

        if (inputCount == 0 || outputCount == 0)
        {
            Array.Clear(output, 0, outputCount * ChannelCount);
            return output;
        }

        for (int iOutput = 0; iOutput < outputCount; iOutput++)
        {
            double iInput = ((double)iOutput / (outputCount - 1)) * (inputCount - 1);
            int iInput0 = (int)iInput;
            int iInput1 = iInput0 + 1;
            double input0Weight = iInput1 - iInput;
            double input1Weight = iInput - iInput0;

            if (iInput1 == inputCount)
                iInput1 = inputCount - 1;

            for (int iChannel = 0; iChannel < ChannelCount; iChannel++)
            {
                double value =
                    input[iInput0 * ChannelCount + iChannel] * input0Weight +
                    input[iInput1 * ChannelCount + iChannel] * input1Weight;

                output[iOutput * ChannelCount + iChannel] = (short)((int)(value + 32768.5) - 32768);
            }
        }

        return output;
    }

    static double Average(List<double> l) {
        double s = 0;
        foreach(double d in l) {
            s += d;
        }
        return s / l.Count;
    }
}

}