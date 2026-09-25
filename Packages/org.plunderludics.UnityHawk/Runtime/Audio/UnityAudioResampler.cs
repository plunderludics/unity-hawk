using System;
using UnityEngine;
using TriInspector;
using UnityHawk.Host;

namespace UnityHawk {

[Serializable]
internal class UnityAudioResampler {
    [Tooltip("Higher value means more audio latency. Lower value may cause crackles and pops")]
    public int idealBufferSize = 512;

    public int movingAverageN = 1024;

    [Tooltip("How much pressure to apply to keep the buffer short. Higher value will reduce latency but can cause pitch distortion")]
    public float excessPressureFactor = 0.01f;

    AudioResampler _resampler;

    [ShowInInspector, ReadOnly]
    int sourceBufferCount => _resampler != null ? _resampler.SourceBufferCount : 0;

    [ShowInInspector]
    float ResampleRatio => _resampler != null ? _resampler.ResampleRatio : 0f;

    public void Init(double defaultResampleRatio, Logger logger) {
        _resampler = new AudioResampler {
            IdealBufferSize = idealBufferSize,
            MovingAverageN = movingAverageN,
            ExcessPressureFactor = excessPressureFactor,
        };
        _resampler.Init(defaultResampleRatio, new HostLog(logger));
    }

    public void SetSourceBuffer(RingBuffer<short> sourceBuffer) {
        _resampler.SetSourceBuffer(sourceBuffer);
    }

    public int GetSamples(float[] samples, int channels, bool multiply = false) {
        _resampler.IdealBufferSize = idealBufferSize;
        _resampler.MovingAverageN = movingAverageN;
        _resampler.ExcessPressureFactor = excessPressureFactor;
        return _resampler.GetSamples(samples, channels, multiply);
    }
}

}
