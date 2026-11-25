using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     Bits into the entropy-coded segment, most significant first, every 0xFF byte followed by a stuffed
///     0x00. Bits gather in a 64-bit register and leave four bytes at a time when none of them is 0xFF,
///     the common case; otherwise one at a time, with the stuffing.
/// </summary>
internal sealed class JpegBitWriter
{
    private const int BufferSize = 16 * 1024;

    private readonly Stream _output;
    private readonly byte[] _buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(BufferSize);
    private int _filled;
    private ulong _bits;
    private int _bitCount;

    public JpegBitWriter(Stream output) => _output = output;

    /// <summary>Appends the low <paramref name="count" /> bits of <paramref name="value" />, count at most 32.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void Write(int value, int count)
    {
        _bits = (_bits << count) | ((uint)value & ((1UL << count) - 1));
        _bitCount += count;
        if (_bitCount >= 32)
            EmitWord();
    }

    /// <summary>Pads the last byte with ones, as the standard says, writes what is buffered and returns the buffer to its pool.</summary>
    public void Finish()
    {
        while (_bitCount >= 8)
        {
            _bitCount -= 8;
            Emit((byte)(_bits >> _bitCount));
        }

        if (_bitCount > 0)
        {
            var padded = (byte)((_bits << (8 - _bitCount)) | ((1u << (8 - _bitCount)) - 1));
            _bitCount = 0;
            Emit(padded);
        }

        Flush();
        System.Buffers.ArrayPool<byte>.Shared.Return(_buffer);
    }

    /// <summary>Takes the top four bytes out of the register: in one write when none is 0xFF.</summary>
    private void EmitWord()
    {
        _bitCount -= 32;
        var word = (uint)(_bits >> _bitCount);
        var inverted = ~word;
        var hasFF = ((inverted - 0x01010101u) & ~inverted & 0x80808080u) != 0;
        if (!hasFF && _filled + 4 <= _buffer.Length)
        {
            BinaryPrimitives.WriteUInt32BigEndian(_buffer.AsSpan(_filled), word);
            _filled += 4;
            return;
        }

        Emit((byte)(word >> 24));
        Emit((byte)(word >> 16));
        Emit((byte)(word >> 8));
        Emit((byte)word);
    }

    private void Emit(byte b)
    {
        if (_filled >= _buffer.Length - 1)
            Flush();
        _buffer[_filled++] = b;
        if (b == 0xFF)
            _buffer[_filled++] = 0x00;
    }

    private void Flush()
    {
        _output.Write(_buffer, 0, _filled);
        _filled = 0;
    }
}
