// Handles capturing audio from Bizhawk and streaming to Unity AudioSource
// (Kind of feel like this should be a separate class rather than a partial,
//  but this is easier for now.)

using UnityEngine;
using NaughtyAttributes;

namespace UnityHawk {

public partial class Emulator {
    [Foldout("Debug")]
    [ShowIf("captureEmulatorAudio")]
    [Tooltip("Higher value means more audio latency. Lower value may cause crackles and pops")]
    public int audioBufferSurplus = (int)(2*44100*0.1); // 0.1s seems to be enough

    static int AudioBufferSize = (int)(2*44100*5); // Size of local audio buffer, 5 sec should be plenty

    short[] _audioBuffer; // circular buffer (queue) to locally store audio samples accumulated from the emulator
    int _audioBufferStart, _audioBufferEnd;
    int _audioSamplesNeeded; // track how many samples unity wants to consume


    // Track how many times we skip audio, log a warning if it's too much
    float _audioSkipCounter;
    float _acceptableSkipsPerSecond = 1f;

    void InitAudio() {
        _audioBuffer = new short[AudioBufferSize];
        _audioSamplesNeeded = 0;
        _audioSkipCounter = 0;
        AudioBufferClear();
    }
    // Should be called in Update() every frame
    void UpdateAudio() {
        // Get latest audio from shared memory
        // Don't want to do this every frame so only do it if more samples are needed
        if (AudioBufferCount() < audioBufferSurplus) {
            CaptureBizhawkAudio();
        }
        _audioSkipCounter += _acceptableSkipsPerSecond*Time.deltaTime;
        if (_audioSkipCounter < 0f) {
            if (Time.realtimeSinceStartup > 5f) { // ignore the first few seconds while bizhawk is starting up
                // Debug.LogWarning("Suffering frequent audio drops (consider increasing audioBufferSurplus value)");
            }
            _audioSkipCounter = 0f;
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
        _audioSamplesNeeded += out_buffer.Length;

        // copy from the local running audio buffer into unity's buffer, convert short to float
        int out_i;
        for (out_i = 0; out_i < out_buffer.Length; out_i++) {
            if (AudioBufferCount() > 0) {
                out_buffer[out_i] = AudioBufferDequeue()/32767f;
            } else {
                // we didn't have enough bizhawk samples to fill the unity audio buffer
                // log a warning if this happens frequently enough
                _audioSkipCounter -= 1f;
                break;
            }
        }
        // Debug.Log($"Consumed {out_i} samples; Current buffer length: {AudioBufferCount()}");
        int lacking = out_buffer.Length - out_i;
        if (lacking > 0) Debug.LogWarning($"Starved of bizhawk samples, filling {lacking} samples with silence");

        // Clear buffer except for a small amount of samples leftover (as buffer against skips/pops)
        // (kind of a dumb way of doing this, could just reset _audioBufferStart but whatever)
        int droppedSamples = 0;
        while (AudioBufferCount() > audioBufferSurplus) {
            _ = AudioBufferDequeue();
            droppedSamples++;
        }
        if (droppedSamples > 0) {
            Debug.LogWarning($"Dropped {droppedSamples} samples from bizhawk");
        }
    }

    // Gets called from Update whenever the local sample buffer is running low
    // Get audio samples (since last call) from Bizhawk, and store them into a buffer
    // to be played back in OnAudioFilterRead
    void CaptureBizhawkAudio() {
        short[] samples = _sharedAudioBuffer.GetSamples();
        if (samples == null) return; // This is fine, sometimes bizhawk just doesn't have any samples ready

        // Append samples to running audio buffer to be played back later
        // [Doing an Array.Copy here instead would probably be way faster but not a big deal]
        for (int i = 0; i < samples.Length; i++) {
            if (AudioBufferCount() == _audioBuffer.Length - 1) {
                Debug.LogWarning("local audio buffer full, dropping samples");
            }
            AudioBufferEnqueue(samples[i]);
        }
    }

    // helper methods for circular audio buffer [should probably be a separate class but whatever]
    private int AudioBufferCount() {
        return (_audioBufferEnd - _audioBufferStart + _audioBuffer.Length)%_audioBuffer.Length;
    }
    private void AudioBufferClear() {
        _audioBufferStart = 0;
        _audioBufferEnd = 0;
    }
    // consume a sample from the queue
    private short AudioBufferDequeue() {
        short s = _audioBuffer[_audioBufferStart];
        _audioBufferStart = (_audioBufferStart + 1)%_audioBuffer.Length;
        return s;
    }
    private void AudioBufferEnqueue(short x) {
        _audioBuffer[_audioBufferEnd] = x;
        _audioBufferEnd++;
        _audioBufferEnd %= _audioBuffer.Length;
    }
    private short GetAudioBufferAt(int i) {
        return _audioBuffer[(_audioBufferStart + i)%_audioBuffer.Length];
    }
}

}