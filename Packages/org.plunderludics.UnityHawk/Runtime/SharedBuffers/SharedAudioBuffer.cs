// Rpc buffer for receiving most recent batch of audio samples from bizhawk
using System;
using UnityEngine;
using SharedMemory;

using System.Collections.Generic;
using System.Collections.Concurrent;

namespace UnityHawk {
public class SharedAudioBuffer : ISharedBuffer {
    string _name;
    RpcBuffer _rpc;

    /// Buffer to accumulate samples in
    /// Has to be thread-safe since it gets modified in rpc call
    /// Manually cap the size (circularly) [Is there a better class to use for this?]
    RingBuffer<short> _localBuffer; 

    Logger _logger;

    // store at most ~0.5s of audio samples (44100Hz with 2 channels)
    const int MaxBufferSize = 44100; // [TODO: this should be a parameter maybe]

    public RingBuffer<short> SampleQueue => _localBuffer; // Expose this so it can be used by AudioResampler directly without having to copy

    public SharedAudioBuffer(string name, Logger logger) {
        _name = name;
        _logger = logger;
        _localBuffer = new(MaxBufferSize);
    }

    public void Open() {
        _rpc = new (
            name: _name,
            (msgId, payload) => {
                ReceiveBizhawkSamples(payload);
            }
        );
    }

    public bool IsOpen() {
        return _rpc != null;
    }

    public void Close() {
        _rpc.Dispose();
        _rpc = null;
    }

    public void ReceiveBizhawkSamples(byte[] bytes) {
        // Bizhawk calls this method each frame to send samples to unity
        if (bytes == null || bytes.Length == 0) {
            _logger.LogVerbose("BizHawk sent empty samples array");
            return;
        }

        // Convert bytes to shorts
        short[] samples = new short[bytes.Length/2];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);

        // Write all samples into local buffer
        _localBuffer.Write(samples, 0, samples.Length);

        _logger.LogVerbose($"Received {samples.Length} samples from bizhawk");
        // Add to local buffer
        // for (int i = 0; i < samples.Length; i++) {
        //     // _localBuffer.Enqueue(samples[i]);
        // }
    }

    // No longer used - AudioResampler gets samples directly from SampleQueue
    // public short[] GetSamples() {
    //     // Read all available samples from local buffer
    //     short[] samples = _localBuffer.ToArray();
    //     _localBuffer.Clear();

    //     return samples;
    // }
}
}