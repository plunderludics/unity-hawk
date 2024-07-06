using UnityEngine;
using NaughtyAttributes;
using System.Collections.Generic;

namespace UnityHawk {

public partial class Emulator {
    [Foldout("Debug")]
    [ShowIf("captureEmulatorAudio")]
    [Tooltip("Higher value means more audio latency. Lower value may cause crackles and pops")]
    public int audioBufferSurplus = (int)(2*44100*0.05);
    static int AudioBufferSize = (int)(2*44100*1); // Size of local audio buffer, 1 sec should be plenty

    // ^ This is the actual 'buffer' part - samples that are retained after passing audio to unity.
    // Smaller surplus -> less latency but more clicks & pops (when bizhawk fails to provide audio in time)
    // 50ms seems to be an ok compromise (but probably depends on host machine, users can configure if needed)
    short[] _audioBuffer; // circular buffer (queue) to locally store audio samples accumulated from the emulator
    int _audioBufferStart, _audioBufferEnd;
    int _audioSamplesNeeded; // track how many samples unity wants to consume
    
    // Track how many times we skip audio, log a warning if it's too much
    float _audioSkipCounter;
    float _acceptableSkipsPerSecond = 1f;

    private Queue<short> _localBuffer;

    void InitAudio() {
        // Init local audio buffer
        _audioBuffer = new short[AudioBufferSize];
        _audioSamplesNeeded = 0;
        _localBuffer = new();
        
        _audioSkipCounter = 0f;
    }
    void UpdateAudio() {
        // request audio buffer over rpc
        // Don't want to do this every frame so only do it if more samples are needed
        if (_localBuffer.Count < audioBufferSurplus) {
            CaptureBizhawkAudio();
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
            if (_localBuffer.Count == _audioBuffer.Length - 1) {
                Debug.LogWarning("local audio buffer full, dropping samples");
            }
            _localBuffer.Enqueue(samples[i]);
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
            if (_localBuffer.Count > 0) {
                out_buffer[out_i] = _localBuffer.Dequeue()/32767f;
            } else {
                // we didn't have enough bizhawk samples to fill the unity audio buffer
                // log a warning if this happens frequently enough
                _audioSkipCounter -= 1f;
                break;
            }
        }
        int lacking = out_buffer.Length - out_i;
        if (lacking > 0) Debug.LogWarning($"Starved of bizhawk samples, generating {lacking} empty samples");

        // Clear buffer except for a small amount of samples leftover (as buffer against skips/pops)
        // (kind of a dumb way of doing this, could just reset _audioBufferEnd but whatever)
        int droppedSamples = 0;
        while (_localBuffer.Count > audioBufferSurplus) {
            _ = _localBuffer.Dequeue();
            droppedSamples++;
        }
        if (droppedSamples > 0) Debug.LogWarning($"Dropped {droppedSamples} samples from bizhawk");
    }

}

}