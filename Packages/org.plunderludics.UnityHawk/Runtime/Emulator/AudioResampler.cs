using UnityEngine;
using NaughtyAttributes;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Collections.Concurrent;
using UnityEditor.EditorTools;

namespace UnityHawk {

[Serializable]
public class AudioResampler {
    [AllowNesting]
    [Tooltip("Higher value means more audio latency. Lower value may cause crackles and pops")]
    public int idealBufferSize = 4096;

    [AllowNesting]
    // [ReadOnly, SerializeField]
    private double _avgSamplesProvided;

    private ConcurrentQueue<short> _rawBuffer;

    [AllowNesting]
    public int movingAverageN = 1024; // Has to be biiig because the input is so unstable
    private List<int> _samplesProvidedHistory;

    [AllowNesting]
    public float excessPressureFactor = 0.01f;

    private int _samplesProvidedThisFrame;

    private double _defaultResampleRatio; // Ratio between bizhawk sample rate and unity sample rate

    private const int ChannelCount = 2;
    
    [AllowNesting]
    [ReadOnly, SerializeField]
    private int rawBufferCount;

    [AllowNesting]
    [ReadOnly, SerializeField]
    private double resampleRatio = 1f;

    private const int maxConsecutiveEmptyFrames = 5; // If we get more empty frames than this, stop processing audio
    private int _consecutiveEmptyFrames = 0;
    private float _unitySampleRate;

    public AudioResampler(double defaultResampleRatio) {
        _rawBuffer = new();
        _samplesProvidedHistory = new();
        _consecutiveEmptyFrames = 0;
        _samplesProvidedThisFrame = 0;
        _defaultResampleRatio = defaultResampleRatio;
    }

    void UpdateAudio() {
        // _audioSkipCounter += _acceptableSkipsPerSecond*Time.deltaTime;
        // if (_audioSkipCounter < 0f) {
        //     if (Time.realtimeSinceStartup > 5f) { // ignore the first few seconds while bizhawk is starting up
        //         Debug.LogWarning("Suffering frequent audio drops (consider increasing idealBufferSize value)");
        //     }
        //     _audioSkipCounter = 0f;
        // }
    }
    public void PushSamples(short [] samples) {
        if (samples == null) return;

        _samplesProvidedThisFrame += samples.Length/ChannelCount;
        // Debug.Log($"Capturing audio, received {_samplesProvidedThisFrame} samples");

        // Append samples to running audio buffer to be played back later
        // [Doing an Array.Copy here instead would probably be way faster but not a big deal]
        for (int i = 0; i < samples.Length; i++) {
            // TODO may want to cap the size of the queue
            _rawBuffer.Enqueue(samples[i]);
        }
        
        rawBufferCount = _rawBuffer.Count/ChannelCount;
    }

    public void GetSamples(float[] out_buffer, int channels) {
        if (channels != 2) {
            Debug.LogError("[unity-hawk] AudioSource must be set to 2 channels");
            return;
        }

        if (_samplesProvidedThisFrame == 0) {
            _consecutiveEmptyFrames++;
            if (_consecutiveEmptyFrames > maxConsecutiveEmptyFrames) {
                // Sometimes bizhawk just stops sending sound (e.g. when paused), we don't want this to affect the moving average resample ratio
                return;
            }
        } else {
            _consecutiveEmptyFrames = 0;
        }

        // Resample
        int stereoSamplesNeeded = out_buffer.Length/ChannelCount;

        _samplesProvidedHistory.Add(_samplesProvidedThisFrame);
        _samplesProvidedThisFrame = 0;
        while (_samplesProvidedHistory.Count > movingAverageN) {
            _samplesProvidedHistory.RemoveAt(0);
        }
        double avgSamplesProvided = Lerp(
            stereoSamplesNeeded*_defaultResampleRatio,
            Average(_samplesProvidedHistory),
            _samplesProvidedHistory.Count/movingAverageN
        );

        _avgSamplesProvided = avgSamplesProvided;

        // Calculate rescale ratio, add to history, then calculate smoothed ratio based on moving average
        double ratio = avgSamplesProvided/stereoSamplesNeeded;
        resampleRatio = ratio;

        int stereoSamplesToConsume = (int)(ratio*stereoSamplesNeeded);
        int availableStereoSamples = _rawBuffer.Count/ChannelCount;

        int excessStereoSamples = availableStereoSamples - stereoSamplesToConsume - idealBufferSize;

        int extraStereoSamplesToConsume = (int)(excessStereoSamples*excessPressureFactor);

        stereoSamplesToConsume += extraStereoSamplesToConsume;

        // Debug.Log($"Want {stereoSamplesToConsume} samples, {availableStereoSamples} are available");
        if (stereoSamplesToConsume > availableStereoSamples) {
            // Debug.LogWarning($"Starved of bizhawk samples");
            stereoSamplesToConsume = availableStereoSamples;
        }

        stereoSamplesToConsume = Math.Max(0, stereoSamplesToConsume);

        // Pop `stereoSamplesToConsume` samples off the buffer
        short[] rawSamples = new short[stereoSamplesToConsume*ChannelCount]; // TODO init elsewhere
        for (int i = 0; i < rawSamples.Length; i++) {
            short x;
            _rawBuffer.TryDequeue(out x);
            rawSamples[i] = x;
        }
        // Debug.Log($"Resampling from {stereoSamplesToConsume} to {stereoSamplesNeeded} ({ratio})");
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

    static double Average(List<int> l) {
        double s = 0;
        foreach(int d in l) {
            s += d;
        }
        return s / l.Count;
    }
    public static double Lerp(double a, double b, double t)
    {
        return a + (b - a);
    }
}

}