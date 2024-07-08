// This implementation is a bit of a mess
// I think the way it should be done instead is just do the resampling stuff on the unity side
// And hopefully just use a simple circular buffer instead of this dual RPC mess
using System;
using UnityEngine;
using SharedMemory;

using System.Collections.Generic;
using System.Collections.Concurrent;
public class SharedAudioBuffer : ISharedBuffer {
    private string _name;
    private RpcBuffer _rpc;

    private ConcurrentQueue<short> _localBuffer;
    
    public SharedAudioBuffer(string name) {
        _name = name;
        _localBuffer = new();
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
            Debug.LogWarning("BizHawk sent empty samples array");
            return;
        }
        // Convert bytes to shorts
        short[] samples = new short[bytes.Length/2];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);

        // Debug.Log($"Received {samples.Length} samples from bizhawk");
        // Add to local buffer
        for (int i = 0; i < samples.Length; i++) {
            _localBuffer.Enqueue(samples[i]);
        }
    }

    public short[] GetSamples() {
        // Read all available samples from local buffer
        short[] samples = _localBuffer.ToArray();
        _localBuffer.Clear();

        return samples;
    }
}