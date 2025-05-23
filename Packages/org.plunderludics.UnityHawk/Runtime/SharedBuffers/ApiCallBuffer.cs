// For calling bizhawk api from Unity
// Implemented as a simple queue rather than RPC
// since we have to queue the calls on the bizhawk side anyway, doing as rpc causes thread safety issues
using UnityEngine;
using SharedMemory;
using Plunderludics.UnityHawk;

public class ApiCallBuffer : ISharedBuffer {
    readonly string _name;

    // [should this be ReadWriteBuffer instead?]
    CircularBuffer _buffer;

    public ApiCallBuffer(string name) {
        _name = name;
    }

    public void Open() {
        _buffer = new (_name);
    }

    public bool IsOpen() {
        return _buffer != null && _buffer.NodeCount > 0;
    }

    public void Close() {
        _buffer.Close();
        _buffer = null;
    }

    public void CallMethod(string methodName, string arg = null) {
        if (!IsOpen()) {
            Debug.LogWarning($"Could not call api method {methodName} since api buffer is not open");
            return;
        }

        MethodCall methodCall = new MethodCall {
            MethodName = methodName,
            Argument = arg ?? string.Empty,
        };

        var bytes = Serialization.Serialize(methodCall);
        var amount = _buffer.Write(bytes, timeout: 0);
        if (amount <= 0) {
            Debug.LogWarning("Failed to write method call to shared buffer");
        }
    }
}