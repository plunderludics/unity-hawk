using System;
using SharedMemory;

namespace UnityHawk.Host {

public class SharedAudioBuffer : ISharedBuffer {
    string _name;
    RpcBuffer _rpc;
    RingBuffer<short> _localBuffer;
    IHostLog _logger;

    const int MaxBufferSize = 44100;

    public RingBuffer<short> SampleQueue => _localBuffer;

    public SharedAudioBuffer(string name, IHostLog logger) {
        _name = name;
        _logger = logger ?? NullHostLog.Instance;
        _localBuffer = new RingBuffer<short>(MaxBufferSize);
    }

    public void Open() {
        _rpc = new RpcBuffer(
            name: _name,
            (msgId, payload) => {
                ReceiveBizhawkSamples(payload);
            }
        );
    }

    public bool IsOpen() {
        return _rpc != null;
    }

    public void Close() {
        _rpc.Dispose();
        _rpc = null;
    }

    public void ReceiveBizhawkSamples(byte[] bytes) {
        if (bytes == null || bytes.Length == 0) {
            return;
        }

        short[] samples = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        _localBuffer.Write(samples, 0, samples.Length);
    }
}

}
