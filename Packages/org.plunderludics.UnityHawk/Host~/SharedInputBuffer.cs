using SharedMemory;
using Plunderludics.UnityHawk.Shared;

namespace UnityHawk.Host {

public class SharedInputBuffer : ISharedBuffer {
    string _name;
    IHostLog _logger;
    CircularBuffer _buffer;

    public SharedInputBuffer(string name, IHostLog logger) {
        _name = name;
        _logger = logger ?? NullHostLog.Instance;
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

    public void Write(InputEvent bie) {
        byte[] serialized = Serialization.Serialize(bie);
        _logger.LogVerbose($"Writing input buffer: {bie}");
        int amount = _buffer.Write(serialized, timeout: 0);
        if (amount <= 0) {
            _logger.LogWarning("Failed to write input event to shared buffer");
        }
    }
}

}
