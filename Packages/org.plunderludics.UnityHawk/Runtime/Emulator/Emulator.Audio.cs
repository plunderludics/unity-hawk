// Handles capturing audio from Bizhawk and streaming to Unity AudioSource
// (Kind of feel like this should be a separate class rather than a partial,
//  but this is easier for now.)

using UnityEngine;
using NaughtyAttributes;

using BizHawk.Emulation.Common;
using BizHawk.Client.Common;

using System.Collections.Generic;

namespace UnityHawk {

public partial class Emulator {
    AudioBufferSoundProvider _audioBufferSoundProvider;
    SoundOutputProvider _bufferedSoundProvider; // Resamples the audio coming through the shared buffer to avoid crackles/pops
    
    [Foldout("Debug")]
    public float fakeVsyncRate = 50; // TODO: We should be able to get this from the emulator, but need to set up another rpc buffer for it
    
    private const int SampleRate = 44100;
    public int SoftCorrectionThresholdSamples = 5 * SampleRate / 1000;
    public int StartupMaxSamplesSurplusDeficit = 10 * SampleRate / 1000;
    public int MaxSamplesSurplus = 50 * SampleRate / 1000;
    public int UsableHistoryLength = 20;
    public int MaxHistoryLength = 60;
    public int SoftCorrectionLength = 240;
    public int BaseMaxConsecutiveEmptyFrames = 1;
    public int BaseSampleRateUsableHistoryLength = 60;
    public int BaseSampleRateMaxHistoryLength = 300;
    public int MinResamplingDistanceSamples = 3;
    public int TargetExtraSamples = (int)(10.0 * SampleRate / 1000.0);

    public int MaxSamplesDeficit;

    private Queue<short> _localAudioBuffer;
    public int audioSurplus = 4410;
    private int _audioSamplesNeeded = 0;



    void InitAudio(SharedAudioBuffer sharedAudioBuffer) {
        _audioBufferSoundProvider = new AudioBufferSoundProvider(sharedAudioBuffer);
        _bufferedSoundProvider = new (
            () => fakeVsyncRate,
            standaloneMode: false // [idk why but standalone mode seems to be less distorted]
        ) {
            BaseSoundProvider = _audioBufferSoundProvider
        };

        _bufferedSoundProvider.LogDebug = true;
        _bufferedSoundProvider.Log = (s) => Debug.Log(s);

        _audioSamplesNeeded = 0;

        _localAudioBuffer = new();
    }

    // Should be called in Update() every frame
    void UpdateAudio() {
        _audioBufferSoundProvider.Update();

        _bufferedSoundProvider.SoftCorrectionThresholdSamples = SoftCorrectionThresholdSamples;
        _bufferedSoundProvider.StartupMaxSamplesSurplusDeficit = StartupMaxSamplesSurplusDeficit;
        _bufferedSoundProvider.MaxSamplesSurplus = MaxSamplesSurplus;
        _bufferedSoundProvider.UsableHistoryLength = UsableHistoryLength;
        _bufferedSoundProvider.MaxHistoryLength = MaxHistoryLength;
        _bufferedSoundProvider.SoftCorrectionLength = SoftCorrectionLength;
        _bufferedSoundProvider.BaseMaxConsecutiveEmptyFrames = BaseMaxConsecutiveEmptyFrames;
        _bufferedSoundProvider.BaseSampleRateUsableHistoryLength = BaseSampleRateUsableHistoryLength;
        _bufferedSoundProvider.BaseSampleRateMaxHistoryLength = BaseSampleRateMaxHistoryLength;
        _bufferedSoundProvider.MinResamplingDistanceSamples = MinResamplingDistanceSamples;
        _bufferedSoundProvider.MaxSamplesDeficit = MaxSamplesDeficit;

        if (_localAudioBuffer.Count < audioSurplus) {
            short[] samples;
            int sampleCount;
            _bufferedSoundProvider.GetSamples(_audioSamplesNeeded/2, out samples, out sampleCount);
            Debug.Log($"UpdateAudio requesting {_audioSamplesNeeded/2} samples, got {sampleCount} ({samples.Length})");

            for (int i = 0; i < sampleCount*2; i++) {
                _localAudioBuffer.Enqueue(samples[i]);
            }
            _audioSamplesNeeded = 0;
        }
    }

    // Send audio from the emulator to the AudioSource
    // (this method gets called by Unity if there is an AudioSource component attached)
    void OnAudioFilterRead(float[] out_buffer, int channels) {
        if (!captureEmulatorAudio) return;
        if (!_sharedAudioBuffer.IsOpen()) return;
        if (channels != 2) {
            Debug.LogError("AudioSource must have 2 channels");
            return;
        }
        // track how many samples we wanna request from bizhawk next time
        _audioSamplesNeeded += out_buffer.Length;

        Debug.Log($"OnAudioFilterRead: need {out_buffer.Length} mon-samples; Current buffer length: {_localAudioBuffer.Count}");

        int out_i;
        for (out_i = 0; out_i < out_buffer.Length; out_i++) {
            if (_localAudioBuffer.Count > 0) {
                out_buffer[out_i] = _localAudioBuffer.Dequeue()/32767f;
            } else {
                break;
            }
        }
        int lacking = out_buffer.Length - out_i;
        if (lacking > 0) Debug.LogWarning($"Starved of bizhawk samples, filling {lacking} samples with silence");

        // Clear buffer except for a small amount of samples leftover (as buffer against skips/pops)
        // (kind of a dumb way of doing this, could just reset _audioBufferEnd but whatever)
        int droppedSamples = 0;
        while (_localAudioBuffer.Count > audioSurplus) {
            _ = _localAudioBuffer.Dequeue();
            droppedSamples++;
        }
        if (droppedSamples > 0) Debug.LogWarning($"Dropped {droppedSamples} samples from bizhawk");

    }
}

}