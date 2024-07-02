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
        // Read all available samples in the circular buffer

        List<short> samplesList = new();

        // Read one sample at a time (this seems very inefficient...)
        while (_buffer.Read<short>(out short sample, timeout: 0) > 0) {
            samplesList.Add(sample);
        }

        Debug.Log($"Reading {samplesList.Count} samples from buffer");
        return samplesList.ToArray();
    }
}