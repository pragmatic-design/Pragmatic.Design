// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     VP8's boolean entropy decoder (RFC 6386, section 7), over bytes the caller keeps pinned. The value
///     holds up to 40 bits ahead of the 8-bit window, loaded four bytes at a time and one at a time near the
///     end. Past the end it reads zeros and sets <see cref="IsPastEnd" />, which is how libwebp detects a
///     truncated partition.
/// </summary>
internal sealed unsafe class Vp8BoolDecoder
{
    private readonly byte* _data;
    private readonly int _length;
    private int _position;
    private ulong _value;
    private uint _range = 255;
    private int _bitCount = -8;

    public Vp8BoolDecoder(byte* data, int length)
    {
        _data = data;
        _length = length;
    }

    public bool IsPastEnd { get; private set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadBit(int probability)
    {
        if (_bitCount < 0)
            Load();
        var split = 1 + (((_range - 1) * (uint)probability) >> 8);
        var bigSplit = (ulong)split << _bitCount;
        int bit;
        if (_value >= bigSplit)
        {
            _range -= split;
            _value -= bigSplit;
            bit = 1;
        }
        else
        {
            _range = split;
            bit = 0;
        }

        var shift = BitOperations.LeadingZeroCount(_range) - 24;
        _range <<= shift;
        _bitCount -= shift;
        return bit;
    }

    /// <summary>An unsigned literal of <paramref name="bits" /> bits, most significant first, each at probability one half.</summary>
    public int ReadLiteral(int bits)
    {
        var value = 0;
        while (bits-- > 0)
            value |= ReadBit(128) << bits;
        return value;
    }

    /// <summary>A literal of <paramref name="bits" /> bits followed by its sign.</summary>
    public int ReadSigned(int bits)
    {
        var value = ReadLiteral(bits);
        return ReadBit(128) == 1 ? -value : value;
    }

    /// <summary>A signed literal of <paramref name="bits" /> bits if the flag before it is set, else zero.</summary>
    public int ReadOptionalSigned(int bits) => ReadBit(128) == 1 ? ReadSigned(bits) : 0;

    private void Load()
    {
        if (_position + 4 <= _length)
        {
            var bytes = _data + _position;
            _value = (_value << 32) | ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            _position += 4;
            _bitCount += 32;
            return;
        }

        while (_bitCount < 0)
        {
            _value <<= 8;
            if (_position < _length)
                _value |= _data[_position++];
            else
                IsPastEnd = true;
            _bitCount += 8;
        }
    }
}
