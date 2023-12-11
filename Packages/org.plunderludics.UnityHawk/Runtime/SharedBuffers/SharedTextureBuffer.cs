using SharedMemory;

public class SharedTextureBuffer : ISharedBuffer{
    private string _name;
    private SharedArray<int> _buffer;
    public int Length => _buffer.Length;
    public SharedTextureBuffer(string name) {
        _name = name;
    }

    public void Open() {
        _buffer = new (_name);
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
}