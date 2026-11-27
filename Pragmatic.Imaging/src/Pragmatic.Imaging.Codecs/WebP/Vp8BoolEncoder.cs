using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     VP8's boolean entropy encoder (RFC 6386, section 7), the inverse of <see cref="Vp8BoolDecoder" />,
///     into a buffer from the shared array pool that grows when full. A byte of 0xFF is held back until the
///     next one shows whether a carry runs into it. An encoder reuses one partition after another:
///     <see cref="Begin" /> starts it empty, and <see cref="Release" /> gives the buffer back.
/// </summary>
internal sealed class Vp8BoolEncoder
{
    private byte[] _bytes = [];
    private int _length;
    private int _range;
    private int _value;
    private int _bits;
    private int _pendingFF;

    /// <summary>Starts an empty partition with room for about <paramref name="expectedBytes" />.</summary>
    public void Begin(int expectedBytes)
    {
        Release();
        _bytes = ArrayPool<byte>.Shared.Rent(Math.Max(expectedBytes, 256));
        _range = 255 - 1; // the range less one, so a split is (range * probability) >> 8
        _value = 0;
        _bits = -8;
        _pendingFF = 0;
    }

    public void Release()
    {
        if (_bytes.Length > 0)
            ArrayPool<byte>.Shared.Return(_bytes);
        _bytes = [];
        _length = 0;
    }

    /// <summary>The bytes written, once <see cref="Finish" /> has been called.</summary>
    public ReadOnlySpan<byte> Written => _bytes.AsSpan(0, _length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PutBit(int bit, int probability)
    {
        var split = (_range * probability) >> 8;
        if (bit != 0)
        {
            _value += split + 1;
            _range -= split + 1;
        }
        else
        {
            _range = split;
        }

        if (_range < 127)
            Renormalize();
    }

    /// <summary>A bit at probability one half.</summary>
    public void PutBitUniform(int bit) => PutBit(bit, 128);

    /// <summary>An unsigned literal of <paramref name="bits" /> bits, most significant first, each at probability one half.</summary>
    public void PutLiteral(int value, int bits)
    {
        while (bits-- > 0)
            PutBit((value >> bits) & 1, 128);
    }

    /// <summary>A flag, and when <paramref name="value" /> is not zero its magnitude in <paramref name="bits" /> bits and its sign.</summary>
    public void PutOptionalSigned(int value, int bits)
    {
        PutBitUniform(value != 0 ? 1 : 0);
        if (value == 0)
            return;
        PutLiteral(Math.Abs(value), bits);
        PutBitUniform(value < 0 ? 1 : 0);
    }

    /// <summary>Writes what is left of the value; the stream can then be read to its end.</summary>
    public void Finish()
    {
        PutLiteral(0, 9 - _bits);
        _bits = 0;
        Flush();
    }

    private void Renormalize()
    {
        // The range is under 127 (as range less one), so it takes 1 to 7 doublings to be 128 or more again.
        var shift = BitOperations.LeadingZeroCount((uint)_range + 1) - 24;
        _range = ((_range + 1) << shift) - 1;
        _value <<= shift;
        _bits += shift;
        if (_bits > 0)
            Flush();
    }

    private void Flush()
    {
        var s = 8 + _bits;
        var bits = _value >> s;
        _value -= bits << s;
        _bits -= 8;
        if ((bits & 0xFF) == 0xFF)
        {
            _pendingFF++;
            return;
        }

        Ensure(_pendingFF + 1);
        if ((bits & 0x100) != 0 && _length > 0)
            _bytes[_length - 1]++;
        var held = (bits & 0x100) != 0 ? (byte)0x00 : (byte)0xFF;
        for (; _pendingFF > 0; _pendingFF--)
            _bytes[_length++] = held;
        _bytes[_length++] = (byte)bits;
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
