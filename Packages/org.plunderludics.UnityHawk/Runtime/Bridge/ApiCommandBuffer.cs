using SharedMemory;
using Plunderludics.UnityHawk.Shared;

namespace Plunderludics.BizHawk.Bridge {

public class ApiCommandBuffer : ISharedBuffer {
    string _name;
    CircularBuffer _buffer;
    IBridgeLog _logger;

    public ApiCommandBuffer(string name, IBridgeLog logger) {
        _name = name;
        _logger = logger ?? NullBridgeLog.Instance;
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

    public void CallMethod(string methodName, string arg) {
        if (!IsOpen()) {
            _logger.LogWarning($"Could not call api method {methodName} since api buffer is not open");
            return;
        }

        MethodCall methodCall = new MethodCall {
            MethodName = methodName,
            Argument = arg ?? string.Empty,
        };
        _logger.LogVerbose($"Attempting api method call: {methodCall}");
        byte[] bytes = Serialization.Serialize(methodCall);
        int amount = _buffer.Write(bytes, timeout: 0);
        if (amount <= 0) {
            _logger.LogWarning("Failed to write method call to shared buffer");
        }
    }
}

}
