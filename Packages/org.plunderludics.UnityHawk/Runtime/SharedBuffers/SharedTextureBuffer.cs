using SharedMemory;

public class SharedTextureBuffer : ISharedBuffer {
    private string _name;
    private string _trueName;
    private int _index;
    private SharedArray<int> _buffer;
    public int Length => _buffer.Length;
    public SharedTextureBuffer(string name) {
        _index = 0;
        _name = name;
        UpdateSize();
    }

    public void Open() {
        _buffer = new (_trueName);
    }

    public bool IsOpen() {
        return _buffer != null && _buffer.Length > 0;
    }

    public void Close() {
        _buffer.Close();
        _buffer = null;
    }

    public void CopyTo(int[] other, int startIndex = 0) {
        _buffer.CopyTo(other, startIndex);
    }

    // This needs to be called after LoadRom since the texture buffer size changes.
    public void UpdateSize() {
        _trueName = $"{_name}-{_index}";
        _index++; // increment index for new buffer
        if (_buffer != null) {
            Close();
        }
    }
}