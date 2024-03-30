// This implementation is a bit of a mess
// I think the way it should be done instead is just do the resampling stuff on the unity side
// And hopefully just use a simple circular buffer instead of this dual RPC mess
using System;

using UnityEngine;
using SharedMemory;
using BizHawk.UnityHawk;
public class SharedAudioBuffer : ISharedBuffer {
    private string _name;
    private RpcBuffer _audioRpcBuffer;
    private RpcBuffer _samplesNeededRpcBuffer;
    Func<int> _getSamplesNeeded;
    public SharedAudioBuffer(string name, Func<int> getSamplesNeeded) {
        _name = name;
        _getSamplesNeeded = getSamplesNeeded;
    }

    public void Open() {
        _audioRpcBuffer = new (name: _name);
        _samplesNeededRpcBuffer = new (
            name: _name + "-samples-needed", // [suffix must match UnityHawkSound.cs in BizHawk - should move this to a constant in shared code]
            (msgId, payload) => {
                // This method should get called once after each emulated frame by bizhawk
                // Returns int _audioSamplesNeeded as a 4 byte array
                int nSamples = _getSamplesNeeded();
                // Debug.Log($"GetSamplesNeeded() RPC call: Returning {nSamples}");
                byte[] data = BitConverter.GetBytes(nSamples);
                return data;
            }
        );
    }

    public bool IsOpen() {
        return _audioRpcBuffer != null && _samplesNeededRpcBuffer != null;
    }

    public void Close() {
        _audioRpcBuffer.Dispose();
        _audioRpcBuffer = null;
        _samplesNeededRpcBuffer.Dispose();
        _samplesNeededRpcBuffer = null;
    }

    // static readonly ProfilerMarker s_BizhawkRpcGetSamples = new ProfilerMarker("BizhawkRpcGetSamples");

    public short[] GetSamples() {
        // s_BizhawkRpcGetSamples.Begin();
        RpcResponse response = _audioRpcBuffer.RemoteRequest(new byte[] {}, timeoutMs: 500);
        // s_BizhawkRpcGetSamples.End();
        if (!response.Success) {
            // This happens sometimes, especially when audioCaptureFramerate is high, i have no idea why
            Debug.LogWarning("Rpc call to get audio from BizHawk failed for some reason");
            return null;
        }

        // convert bytes into short
        byte[] bytes = response.Data;
        if (bytes == null || bytes.Length == 0) {
            // This is fine, sometimes bizhawk just doesn't have any samples ready
            return null;
        }
        short[] samples = new short[bytes.Length/2];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        // Debug.Log($"Got audio over rpc: {samples.Length} samples");
        // Debug.Log($"first = {samples[0]}; last = {samples[samples.Length-1]}");
        return samples;
    }
}