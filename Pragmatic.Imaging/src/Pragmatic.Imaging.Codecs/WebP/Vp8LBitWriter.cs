using System.Buffers;
using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The bit writer of the lossless format: least significant bit first, into a buffer from the shared
///     array pool that grows when full. Dispose it to give the buffer back.
/// </summary>
internal sealed class Vp8LBitWriter : IDisposable
{
    private byte[] _bytes;
    private int _length;
    private ulong _buffer;
    private int _bits;

    public Vp8LBitWriter(int expectedBytes) => _bytes = ArrayPool<byte>.Shared.Rent(Math.Max(expectedBytes, 64));

    /// <summary>The bytes written so far, once <see cref="Flush" /> has written the last partial byte.</summary>
    public ReadOnlySpan<byte> Written => _bytes.AsSpan(0, _length);

    /// <summary>Writes the low <paramref name="count" /> bits of <paramref name="value" />, at most 32.</summary>
    public void Write(uint value, int count)
    {
        _buffer |= (ulong)value << _bits;
        _bits += count;
        if (_bits >= 32)
            Spill();
    }

    /// <summary>Appends whole bytes; the writer must be at a byte boundary, as it is when empty.</summary>
    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        if (_bits % 8 != 0)
            throw new InvalidOperationException("The bit writer is not at a byte boundary.");
        Flush();
        Ensure(bytes.Length);
        bytes.CopyTo(_bytes.AsSpan(_length));
        _length += bytes.Length;
    }

    /// <summary>Pads the last byte with zeros and writes what is left.</summary>
    public void Flush()
    {
        while (_bits > 0)
        {
            Ensure(1);
            _bytes[_length++] = (byte)_buffer;
            _buffer >>= 8;
            _bits = Math.Max(0, _bits - 8);
        }
    }

    public void Dispose() => ArrayPool<byte>.Shared.Return(_bytes);

    private void Spill()
    {
        Ensure(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(_length), (uint)_buffer);
        _length += 4;
        _buffer >>= 32;
        _bits -= 32;
    }

    private void Ensure(int more)
    {
        if (_length + more <= _bytes.Length)
            return;
        var larger = ArrayPool<byte>.Shared.Rent(Math.Max(_bytes.Length * 2, _length + more));
        _bytes.AsSpan(0, _length).CopyTo(larger);
        ArrayPool<byte>.Shared.Return(_bytes);
        _bytes = larger;
    }
}
