// Fixed-capacity circular buffer implementation used by SharedAudioBuffer & AudioResampler
// Thread-safe for single producer and single consumer

using System.Threading;

namespace UnityHawk {
public class RingBuffer<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _tail;
    private readonly int _capacity;

    public RingBuffer(int capacity)
    {
        _buffer = new T[capacity];
        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            int count = _head - _tail;
            if (count < 0)
                count += _capacity;
            return count;
        }
    }

    public int AvailableWrite => _capacity - Count - 1;

    public void Write(T[] source, int offset, int length) {
        int written = 0;
        for (int i = 0; i < length && AvailableWrite > 0; i++) {
            int nextHead = (_head + 1) % _capacity;

            if (nextHead == Volatile.Read(ref _tail)) {
                // Buffer full, drop oldest
                _tail = (_tail + 1) % _capacity;
            }

            _buffer[_head] = source[offset + i];
            _head = nextHead;
        }
    }

    public int Read(T[] dest, int offset, int length){
        int read = 0;
        for (int i = 0; i < length && Count > 0; i++)
        {
            dest[offset + i] = _buffer[_tail];
            _tail = (_tail + 1) % _capacity;
            read++;
        }
        return read;
    }
}
}