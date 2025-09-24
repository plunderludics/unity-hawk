using UnityEngine;
using NaughtyAttributes;
using System.Collections.Generic;
using System;
using System.Collections.Concurrent;

namespace UnityHawk {

[Serializable]
internal class AudioResampler {
    [AllowNesting]
    [Tooltip("Higher value means more audio latency. Lower value may cause crackles and pops")]
    public int idealBufferSize = 512; // [TODO: this should be much less (maybe half or less) than SharedAudioBuffer.MaxBufferSize - should probably enforce somehow]

    [AllowNesting]
    // [ReadOnly, SerializeField]
    private double _avgSamplesProvided;

    private RingBuffer<short> _sourceBuffer;
    public bool HasSourceBuffer => _sourceBuffer != null;

    [AllowNesting]
    public int movingAverageN = 1024; // Has to be biiig because the input is so unstable
    private List<int> _samplesProvidedHistory;

    [AllowNesting]
    [Tooltip("How much pressure to apply to keep the buffer short. Higher value will reduce latency but can cause pitch distortion")]
    public float excessPressureFactor = 0.01f;

    private int _samplesProvidedThisFrame;

    private double _defaultResampleRatio; // Ratio between bizhawk sample rate and unity sample rate

    private const int ChannelCount = 2;
    
    [AllowNesting]
    [ReadOnly, SerializeField]
    private int sourceBufferCount;

    [AllowNesting]
    [ReadOnly, SerializeField]
    private double resampleRatio = 1f;

    private const int ConsecutiveEmptyFramesToStopAudio = 5; // If we get more empty frames than this, stop processing audio
    private int _consecutiveEmptyFrames = 0;

    private int _inputSampleDeficit;
    private const int DeficitSampleCountForWarning = 4096; // Warn after accumulating a deficit of this many samples

    int _samplesConsumedLastFrame;

    Logger _logger;

    /// Restart resampler from clean slate
    public void Init(double defaultResampleRatio, Logger logger) {
        _logger = logger;

        _defaultResampleRatio = defaultResampleRatio;
        // _sourceBuffer = new();
        _sourceBuffer = null;
        _samplesProvidedHistory = new();
        _consecutiveEmptyFrames = 0;
        _inputSampleDeficit = 0;
        _samplesConsumedLastFrame = 0;
    }

    // This slightly awkward structure is to avoid having to have two separate sample buffers and copy between them
    // - we just read directly from the samples accumulated in SharedAudioBuffer
    public void SetSourceBuffer(RingBuffer<short> sourceBuffer) {
        _sourceBuffer = sourceBuffer;
    }

    // Length of samples array specifies how many samples are being requested
    // Multichannel samples are interleaved
    // If multiply == true, the resampled audio from the source buffer is multiplied with whatever is already in the samples array
    // If false will overwrite
    public void GetSamples(float[] samples, int channels, bool multiply = false) {
        if (_sourceBuffer == null) {
            _logger.LogError("[unity-hawk] AudioResampler source buffer has not been set");
            return;
        }

        if (channels != 2) {
            _logger.LogError("[unity-hawk] AudioSource must be set to 2 channels");
            return;
        }

        int newSourceBufferCount = _sourceBuffer.Count/ChannelCount;
        int newSamplesThisFrame = newSourceBufferCount - sourceBufferCount + _samplesConsumedLastFrame; // Number of samples added since last GetSamples call (ie last OnAudioFilterRead call)
        // (Above calculation will be incorrect when buffer is full, but that's good -
        //  Afaik the only case where this happens is when unity is paused or hangs for some reason, and in that case
        //  we want samples accumulated above the length of the buffer to get dropped and not affect resampling ratio.
        // TODO: I guess if we want to handle that case better we could track the duration between OnAudioFilterRead calls but it seems minor
        sourceBufferCount = newSourceBufferCount;

        if (newSamplesThisFrame == 0) {
            _consecutiveEmptyFrames++;
            if (_consecutiveEmptyFrames > ConsecutiveEmptyFramesToStopAudio) {
                // Sometimes bizhawk just stops sending sound (e.g. when paused), we don't want this to affect the moving average resample ratio
                return;
            }
        } else {
            _consecutiveEmptyFrames = 0;
        }

        // Resample
        int stereoSamplesNeeded = samples.Length/ChannelCount;

        _samplesProvidedHistory.Add(newSamplesThisFrame);
        while (_samplesProvidedHistory.Count > movingAverageN) {
            _samplesProvidedHistory.RemoveAt(0);
        }
        double avgSamplesProvided = Lerp(
            stereoSamplesNeeded*_defaultResampleRatio,
            Average(_samplesProvidedHistory),
            (float)_samplesProvidedHistory.Count/movingAverageN
        );

        _avgSamplesProvided = avgSamplesProvided;

        // Calculate rescale ratio, add to history, then calculate smoothed ratio based on moving average
        double ratio = avgSamplesProvided/stereoSamplesNeeded;
        resampleRatio = ratio;

        int stereoSamplesToConsume = (int)(ratio*stereoSamplesNeeded);
        int availableStereoSamples = sourceBufferCount;

        int excessStereoSamples = availableStereoSamples - stereoSamplesToConsume - idealBufferSize;

        int extraStereoSamplesToConsume = (int)(excessStereoSamples*excessPressureFactor);

        stereoSamplesToConsume += extraStereoSamplesToConsume;

        // _logger.LogVerbose($"Want {stereoSamplesToConsume} samples, {availableStereoSamples} are available");
        if (stereoSamplesToConsume > availableStereoSamples) {
            _inputSampleDeficit += stereoSamplesToConsume - availableStereoSamples;
            if (_inputSampleDeficit > DeficitSampleCountForWarning) {
                _logger.LogWarning($"Starved of audio samples, consider increasing idealBufferSize");
                _inputSampleDeficit = 0;
            }
            stereoSamplesToConsume = availableStereoSamples;
        }

        stereoSamplesToConsume = Math.Max(0, stereoSamplesToConsume);

        // Pop `stereoSamplesToConsume` samples off the buffer
        short[] rawSamples = new short[stereoSamplesToConsume*ChannelCount]; // TODO alloc elsewhere
        _sourceBuffer.Read(rawSamples, 0, rawSamples.Length);

        _samplesConsumedLastFrame = stereoSamplesToConsume;

        // _logger.LogVerbose($"Resampling from {stereoSamplesToConsume} to {stereoSamplesNeeded} ({ratio})");
        short[] resampled = Resample(rawSamples, stereoSamplesToConsume, stereoSamplesNeeded);

        // copy from the local running audio buffer into unity's buffer, convert short to float
        int out_i;
        for (out_i = 0; out_i < samples.Length; out_i++) {
            if (out_i < resampled.Length) {
                float sample = resampled[out_i]/32767f;
                if (multiply) {
                    samples[out_i] *= sample;
                } else {
                    samples[out_i] = sample;
                }
            } else {
                _logger.LogError("[unity-hawk] Ran out of resampled audio, this should never happen");
                break;
            }
        }
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
        return a + (b - a)*t;
    }
}

}