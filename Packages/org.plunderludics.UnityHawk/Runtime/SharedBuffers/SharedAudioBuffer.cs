// This implementation is a bit of a mess
// I think the way it should be done instead is just do the resampling stuff on the unity side
// And hopefully just use a simple circular buffer instead of this dual RPC mess
using System;
using UnityEngine;
using SharedMemory;

using System.Collections.Generic;
public class SharedAudioBuffer : ISharedBuffer {
    private string _name;
    private CircularBuffer _buffer;
    
    public SharedAudioBuffer(string name) {
        _name = name;
    }

    public void Open() {
        _buffer = new CircularBuffer(_name);
    }

    public bool IsOpen() {
        return _buffer != null && _buffer.NodeCount > 0;
    }

    public void Close() {
        _buffer.Close();
        _buffer = null;
    }

    public short[] GetSamples() {
        // Read all available samples from shared memory
        short[] bigBuffer = new short[1000000];

        // Debug.Log($">>");
        int amount;
        int totalAmount = 0;
        while ((amount = _buffer.Read<short>(bigBuffer, startIndex: totalAmount, timeout: 0)) > 0) {
            // Debug.Log($"Read {amount} samples from buffer");
            totalAmount += amount;
        }
        // Debug.Log($"Read {totalAmount} samples from sharedmemory buffer");

        // Debug.Log($"<<");
        short[] samples = new short[totalAmount];
        Array.Copy(bigBuffer, 0, samples, 0, totalAmount);

        return samples;
    }
}