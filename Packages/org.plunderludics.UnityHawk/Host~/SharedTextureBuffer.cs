using SharedMemory;
using Plunderludics.UnityHawk.Shared;

namespace UnityHawk.Host {

public class SharedTextureBuffer : ISharedBuffer {
    string _name;
    string _trueName;
    int _index;
    SharedArray<int> _buffer;
    IHostLog _logger;

    public int Length => _buffer.Length;

    public SharedTextureBuffer(string name, IHostLog logger) {
        _index = 0;
        _name = name;
        _logger = logger ?? NullHostLog.Instance;
        UpdateSize();
    }

    public void Open() {
        _buffer = new SharedArray<int>(_trueName);
    }

    public bool IsOpen() {
        return _buffer != null && _buffer.Length > 0;
    }

    public void Close() {
        _buffer.Close();
        _buffer = null;
    }

    public int Width => _buffer[_buffer.Length - 1 - TextureBufferLayout.WidthIndexFromEnd];
    public int Height => _buffer[_buffer.Length - 1 - TextureBufferLayout.HeightIndexFromEnd];
    public int Frame => _buffer[_buffer.Length - 1 - TextureBufferLayout.FrameIndexFromEnd];

    public int PixelDataLength => _buffer.Length - TextureBufferLayout.MetadataLength;

    public void CopyPixelsTo(int[] other) {
        _buffer.CopyTo(other, startIndex: 0);
    }

    public void UpdateSize() {
        _trueName = $"{_name}-{_index}";
        _index++;
        if (_buffer != null) {
            Close();
        }
    }
}

}
