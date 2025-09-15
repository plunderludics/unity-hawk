using SharedMemory;
using Plunderludics.UnityHawk.Shared;
using UnityEngine;
using System.Linq;

namespace UnityHawk {

internal class SharedTextureBuffer : ISharedBuffer {
    string _name;
    string _trueName;
    int _index;
    SharedArray<int> _buffer;
    Logger _logger;

    public int Length => _buffer.Length;

    public SharedTextureBuffer(string name, Logger logger) {
        _index = 0;
        _name = name;
        _logger = logger;
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

    public int Width => _buffer[_buffer.Length - 1 - TextureBufferLayout.WidthIndexFromEnd];
    public int Height => _buffer[_buffer.Length - 1 - TextureBufferLayout.HeightIndexFromEnd];
    public int Frame => _buffer[_buffer.Length - 1 - TextureBufferLayout.FrameIndexFromEnd];

    public int PixelDataLength => _buffer.Length - TextureBufferLayout.MetadataLength;
    // TODO: Maybe could save a bit of time by only copying (Width*Height*4) ints instead of the whole buffer
    // [Unclear why the bizhawk video buffer is so much longer than the actual pixel data]

    public void CopyPixelsTo(int[] other) {
        // // Debug: Write entire texture buffer to file
        // string filePath = $"texture-dump.txt";
        // _logger.Log($"Writing texture buffer {_trueName} to file {filePath}");
        // System.IO.File.WriteAllLines(filePath, _buffer.Select(value => $"{value}"));
    
        _buffer.CopyTo(other, startIndex: 0);
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