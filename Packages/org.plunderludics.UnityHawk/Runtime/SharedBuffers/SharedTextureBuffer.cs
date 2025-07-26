using SharedMemory;
using Plunderludics.UnityHawk.Shared;
using UnityEngine;
using System.Linq;

namespace UnityHawk {
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

    public int Width => _buffer[TextureBufferLayout.WidthIndex];
    public int Height => _buffer[TextureBufferLayout.HeightIndex];
    public int Frame => _buffer[TextureBufferLayout.FrameIndex];

    public int PixelDataLength => _buffer.Length - TextureBufferLayout.PixelDataStartIndex;
    // TODO: Maybe could save a bit of time by only copying (Width*Height*4) ints instead of the whole buffer
    // [Unclear why the bizhawk video buffer is so much longer than the actual pixel data]

    // Only copy the pixel data, not the metadata
    public void CopyPixelsTo(int[] other) {
        // // Debug: Write entire texture buffer to file
        // string filePath = $"texture-dump.txt";
        // Debug.Log($"Writing texture buffer {_trueName} to file {filePath}");
        // System.IO.File.WriteAllLines(filePath, _buffer.Select(value => $"{value}"));
    
        _buffer.CopyTo(other, TextureBufferLayout.PixelDataStartIndex);
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
}