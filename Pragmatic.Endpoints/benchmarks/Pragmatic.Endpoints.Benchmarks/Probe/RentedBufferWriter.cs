using System.Buffers;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     A buffer that starts small and grows by renting, as the one <c>JsonSerializer.SerializeToUtf8Bytes</c> writes
///     into: for a row that differs from <see cref="ArrayBufferWriter{T}" /> only in where the bytes go.
/// </summary>
internal sealed class RentedBufferWriter(int initialCapacity) : IBufferWriter<byte>
{
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
    private int _written;

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    public void Advance(int count) => _written += count;

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsSpan(_written);
    }

    /// <summary>Gives the buffer back and starts again from a small one.</summary>
    public void Reset(int initialCapacity)
    {
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        _written = 0;
    }

    private void Ensure(int sizeHint)
    {
        var needed = Math.Max(sizeHint, 1);
        if (_buffer.Length - _written >= needed)
            return;

        var next = ArrayPool<byte>.Shared.Rent(Math.Max(_buffer.Length * 2, _written + needed));
        _buffer.AsSpan(0, _written).CopyTo(next);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = next;
    }
}
