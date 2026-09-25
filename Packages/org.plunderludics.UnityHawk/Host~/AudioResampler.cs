using System;
using System.Collections.Generic;

namespace UnityHawk.Host {

public class AudioResampler {
    public int IdealBufferSize = 512;
    public int MovingAverageN = 1024;
    public float ExcessPressureFactor = 0.01f;

    RingBuffer<short> _sourceBuffer;
    List<int> _samplesProvidedHistory;
    double _defaultResampleRatio;
    double _resampleRatio = 1;
    int _consecutiveEmptyFrames;
    int _inputSampleDeficit;
    int _samplesConsumedLastFrame;
    int _sourceBufferCount;
    IHostLog _logger;

    const int ChannelCount = 2;
    const int ConsecutiveEmptyFramesToStopAudio = 5;
    const int DeficitSampleCountForWarning = 4096;

    public bool HasSourceBuffer => _sourceBuffer != null;
    public int SourceBufferCount => _sourceBufferCount;
    public float ResampleRatio => (float)_resampleRatio;

    public void Init(double defaultResampleRatio, IHostLog logger) {
        _logger = logger ?? NullHostLog.Instance;
        _defaultResampleRatio = defaultResampleRatio;
        _sourceBuffer = null;
        _samplesProvidedHistory = new List<int>();
        _consecutiveEmptyFrames = 0;
        _inputSampleDeficit = 0;
        _samplesConsumedLastFrame = 0;
    }

    public void SetSourceBuffer(RingBuffer<short> sourceBuffer) {
        _sourceBuffer = sourceBuffer;
    }

    public int GetSamples(float[] samples, int channels, bool multiply = false) {
        if (_sourceBuffer == null) {
            _logger.LogError("[unity-hawk] AudioResampler source buffer has not been set");
            return 0;
        }

        if (channels != 2) {
            _logger.LogError("[unity-hawk] AudioSource must be set to 2 channels");
            return 0;
        }

        int newSourceBufferCount = _sourceBuffer.Count / ChannelCount;
        int newSamplesThisFrame = newSourceBufferCount - _sourceBufferCount + _samplesConsumedLastFrame;
        _sourceBufferCount = newSourceBufferCount;

        if (newSamplesThisFrame == 0) {
            _consecutiveEmptyFrames++;
            if (_consecutiveEmptyFrames > ConsecutiveEmptyFramesToStopAudio) {
                return 0;
            }
        } else {
            _consecutiveEmptyFrames = 0;
        }

        int stereoSamplesNeeded = samples.Length / ChannelCount;

        _samplesProvidedHistory.Add(newSamplesThisFrame);
        while (_samplesProvidedHistory.Count > MovingAverageN) {
            _samplesProvidedHistory.RemoveAt(0);
        }
        double avgSamplesProvided = Lerp(
            stereoSamplesNeeded * _defaultResampleRatio,
            Average(_samplesProvidedHistory),
            (float)_samplesProvidedHistory.Count / MovingAverageN
        );

        double ratio = avgSamplesProvided / stereoSamplesNeeded;
        _resampleRatio = ratio;

        int stereoSamplesToConsume = (int)(ratio * stereoSamplesNeeded);
        int availableStereoSamples = _sourceBufferCount;
        int excessStereoSamples = availableStereoSamples - stereoSamplesToConsume - IdealBufferSize;
        int extraStereoSamplesToConsume = (int)(excessStereoSamples * ExcessPressureFactor);
        stereoSamplesToConsume += extraStereoSamplesToConsume;

        if (stereoSamplesToConsume > availableStereoSamples) {
            _inputSampleDeficit += stereoSamplesToConsume - availableStereoSamples;
            if (_inputSampleDeficit > DeficitSampleCountForWarning) {
                _logger.LogWarning("Starved of audio samples, consider increasing idealBufferSize");
                _inputSampleDeficit = 0;
            }
            stereoSamplesToConsume = availableStereoSamples;
        }

        stereoSamplesToConsume = Math.Max(0, stereoSamplesToConsume);

        short[] rawSamples = new short[stereoSamplesToConsume * ChannelCount];
        _sourceBuffer.Read(rawSamples, 0, rawSamples.Length);
        _samplesConsumedLastFrame = stereoSamplesToConsume;

        short[] resampled = Resample(rawSamples, stereoSamplesToConsume, stereoSamplesNeeded);

        for (int outIndex = 0; outIndex < samples.Length; outIndex++) {
            if (outIndex < resampled.Length) {
                float sample = resampled[outIndex] / 32767f;
                if (multiply) {
                    samples[outIndex] *= sample;
                } else {
                    samples[outIndex] = sample;
                }
            } else {
                _logger.LogError("[unity-hawk] Ran out of resampled audio, this should never happen");
                break;
            }
        }

        return stereoSamplesToConsume;
    }

    static short[] Resample(short[] input, int inputCount, int outputCount) {
        if (inputCount == outputCount) {
            return input;
        }

        short[] output = new short[outputCount * ChannelCount];
        if (inputCount == 0 || outputCount == 0) {
            Array.Clear(output, 0, outputCount * ChannelCount);
            return output;
        }

        for (int iOutput = 0; iOutput < outputCount; iOutput++) {
            double iInput = ((double)iOutput / (outputCount - 1)) * (inputCount - 1);
            int iInput0 = (int)iInput;
            int iInput1 = iInput0 + 1;
            double input0Weight = iInput1 - iInput;
            double input1Weight = iInput - iInput0;

            if (iInput1 == inputCount)
                iInput1 = inputCount - 1;

            for (int iChannel = 0; iChannel < ChannelCount; iChannel++) {
                double value =
                    input[iInput0 * ChannelCount + iChannel] * input0Weight +
                    input[iInput1 * ChannelCount + iChannel] * input1Weight;
                output[iOutput * ChannelCount + iChannel] = (short)((int)(value + 32768.5) - 32768);
            }
        }

        return output;
    }

    static double Average(List<int> values) {
        double sum = 0;
        foreach (int value in values) {
            sum += value;
        }
        return sum / values.Count;
    }

    public static double Lerp(double a, double b, double t) {
        return a + (b - a) * t;
    }
}

}
