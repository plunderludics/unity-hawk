// The idea is this is for write-only commands to bizhawk where we don't need any return value
// These get processed at the beginning of each frame by bizhawk
// ApiRpcBuffer is for read/write commands where we need a return value
// But that runs in a separate thread on the bizhawk side, might cause timing/threading issues so seems preferable to use a simple queue when possible

using UnityEngine;
using SharedMemory;
using Plunderludics.UnityHawk;

namespace UnityHawk {

public class ApiCommandBuffer : ISharedBuffer {
    private string _name;
    private CircularBuffer _buffer;
    public ApiCommandBuffer(string name) {
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
    public void CallMethod(string methodName, string arg) {
        if (!IsOpen()) {
            Debug.LogWarning($"Could not call api method {methodName} since api buffer is not open");
            return;
        }

        MethodCall methodCall = new MethodCall {
            MethodName = methodName,
            Argument = arg
        };
        // Debug.Log($"Attempting api method call: {methodCall}");
        byte[] bytes = Serialization.Serialize(methodCall);
        int amount = _buffer.Write(bytes, timeout: 0);
        if (amount <= 0) {
            Debug.LogWarning("Failed to write method call to shared buffer");
        }
    }
}
}