using System;
using BizHawk.Emulation.Common;
using BizHawk.Client.Common;

using UnityEngine;
public class AudioBufferSoundProvider: ISoundProvider {
    const int ChannelCount = 2;
    SharedAudioBuffer _sharedAudioBuffer;
    short[] _localBuffer;
    int _localBufferCount;
    const int BufferSize = (int)(2*44100*5);
    const int MaxSamplesPerFrame = 409600; // TODO remove

    public AudioBufferSoundProvider(SharedAudioBuffer sharedAudioBuffer) {
        _sharedAudioBuffer = sharedAudioBuffer;
        _localBuffer = new short[BufferSize];
        _localBufferCount = 0;
    }

    public void Update() {
        // Accumulate samples from shared memory into local buffer
        short[] samples = _sharedAudioBuffer.GetSamples();
        Array.Copy(samples, 0, _localBuffer, _localBufferCount, samples.Length);
        _localBufferCount += samples.Length;
    }

	public bool CanProvideAsync => false;

    public void SetSyncMode(SyncSoundMode mode) {
        if (mode == SyncSoundMode.Async) {
            throw new InvalidOperationException();
        }
    }
    
	public SyncSoundMode SyncMode => SyncSoundMode.Sync;

	public void GetSamplesSync(out short[] samples, out int nsamp) {
        int nSamples = Math.Min(_localBufferCount, MaxSamplesPerFrame);
        samples = new short[nSamples];
        Array.Copy(_localBuffer, 0, samples, 0, nSamples);
        _localBufferCount -= nSamples;
        Debug.Log($"Flushing {nSamples} samples, {_localBufferCount} remaining");
        nsamp = samples.Length/ChannelCount;
    }

    public void GetSamplesAsync(short[] samples) {
        throw new InvalidOperationException();
    }

	public void DiscardSamples() {
        // TODO
    }
}