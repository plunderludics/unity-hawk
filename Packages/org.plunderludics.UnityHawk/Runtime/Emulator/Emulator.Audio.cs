using UnityEngine;
using NaughtyAttributes;
using System.Collections.Generic;
using System;

namespace UnityHawk {

public partial class Emulator {
    [Foldout("Debug")]
    [ShowIf("captureEmulatorAudio")]
    [Tooltip("Higher value means more audio latency. Lower value may cause crackles and pops")]
    public int audioBufferSurplus = (int)(2*44100*0.05);
    
    [Foldout("Debug")]
    [ShowIf("captureEmulatorAudio")]
    [Tooltip("Higher value means smoother audio but more latency")]
    public int minCountToResample = 2048*16; // Size of chunk for resampling audio

    [Foldout("Debug")]
    [ShowIf("captureEmulatorAudio")]
    [ReadOnly, SerializeField]
    private int resampledBufferCount;

    static int RawBufferSize = (int)(2*44100*1); // Size of local audio buffer, 1 sec should be plenty
    static int ResampledBufferSize = (int)(2*44100*1); // Size of local audio buffer, 1 sec should be plenty
    
    private Queue<short> _rawBuffer;
    private Queue<short> _resampledBuffer;

    [Foldout("Debug")]
    [ShowIf("captureEmulatorAudio")]
    public int movingAverageN = 5;
    private List<double> _resampleRatios;


    int _stereoSamplesNeeded; // track how many samples unity wants to consume
    
    int _stereoSamplesNeededSinceForever;
    int _audioSamplesProvidedSinceForever;

    // Track how many times we skip audio, log a warning if it's too much
    float _audioSkipCounter;
    float _acceptableSkipsPerSecond = 1f;


    private const int ChannelCount = 2;

    void InitAudio() {
        // Init local audio buffer
        _resampledBuffer = new();
        _rawBuffer = new();
        
        _audioSkipCounter = 0f;
        _stereoSamplesNeededSinceForever = 0;
        _audioSamplesProvidedSinceForever = 0;

        _resampleRatios = new();
    }
    void UpdateAudio() {
        CaptureBizhawkAudio(); // Probably don't need to do this every frame, but if it's fast enough it's fine

        // If we've accumulated enough audio from bizhawk, resample it and append to resampled buffer
        if (_rawBuffer.Count >= minCountToResample) {
            // Dump all samples from queue into array
            short[] rawSamples = new short[_rawBuffer.Count]; // TODO init elsewhere
            for (int i = 0; i < rawSamples.Length; i++) {
                rawSamples[i] = _rawBuffer.Dequeue();
            }

            // Resample
            int stereoSamplesProvided = rawSamples.Length/ChannelCount;
            double ratio = (double)_stereoSamplesNeeded/stereoSamplesProvided;
            _resampleRatios.Add(ratio);
            while (_resampleRatios.Count > movingAverageN) {
                _resampleRatios.RemoveAt(0);
            }
            ratio = Average(_resampleRatios);

            int targetCount =  (int)(ratio*stereoSamplesProvided);
            Debug.Log($"Resampling from {stereoSamplesProvided} to {targetCount} ({ratio})");
            short[] resampled = Resample(rawSamples, stereoSamplesProvided, targetCount);
            _stereoSamplesNeeded = 0;

            // Add to buffer
            for (int i = 0; i < resampled.Length; i++) {
                _resampledBuffer.Enqueue(resampled[i]);
            }
        }

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

        // Append samples to running audio buffer to be played back later
        // [Doing an Array.Copy here instead would probably be way faster but not a big deal]
        for (int i = 0; i < samples.Length; i++) {
            // TODO may want to cap the size of the queue
            _rawBuffer.Enqueue(samples[i]);
            _audioSamplesProvidedSinceForever++;
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

        // track how many samples we wanna request from bizhawk next time
        _stereoSamplesNeeded += out_buffer.Length/ChannelCount;
        _stereoSamplesNeededSinceForever += out_buffer.Length/ChannelCount;

        // copy from the local running audio buffer into unity's buffer, convert short to float
        int out_i;
        for (out_i = 0; out_i < out_buffer.Length; out_i++) {
            if (_resampledBuffer.Count > 0) {
                out_buffer[out_i] = _resampledBuffer.Dequeue()/32767f;
            } else {
                // we didn't have enough bizhawk samples to fill the unity audio buffer
                // log a warning if this happens frequently enough
                _audioSkipCounter -= 1f;
                break;
            }
        }
        int lacking = out_buffer.Length - out_i;
        if (lacking > 0) Debug.LogWarning($"Starved of bizhawk samples, generating {lacking} empty samples");

        resampledBufferCount = _resampledBuffer.Count;

        // Clear buffer except for a small amount of samples leftover (as buffer against skips/pops)
        // (kind of a dumb way of doing this, could just reset _audioBufferEnd but whatever)
        int droppedSamples = 0;
        while (_resampledBuffer.Count > audioBufferSurplus) {
            _ = _resampledBuffer.Dequeue();
            droppedSamples++;
        }
        if (droppedSamples > 0) Debug.LogWarning($"Dropped {droppedSamples} samples from bizhawk");
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