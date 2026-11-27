// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The bit reader of the lossless format: least significant bit first, over bytes the caller keeps
///     pinned. The buffer is refilled eight bytes at a time while there are eight to read. Reading past the
///     end yields zeros, the way libwebp's reader does, and is reported by <see cref="IsPastEnd" /> so the
///     decoder can refuse a truncated stream.
/// </summary>
internal sealed unsafe class Vp8LBitReader
{
    private byte* _data;
    private int _length;
    private int _position;
    private ulong _buffer;
    private int _bits;
    private long _consumed;

    /// <summary>Starts over on <paramref name="length" /> bytes at <paramref name="data" />, as a reader just made on them.</summary>
    public void Reset(byte* data, int length)
    {
        _data = data;
        _length = length;
        _position = 0;
        _buffer = 0;
        _bits = 0;
        _consumed = 0;
    }

    /// <summary>Whether more bits were consumed than the stream holds.</summary>
    public bool IsPastEnd => _consumed > (long)_length * 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadBits(int count)
    {
        if (_bits < count)
            Fill();
        var value = (uint)(_buffer & ((1UL << count) - 1));
        _buffer >>= count;
        _bits -= count;
        _consumed += count;
        return value;
    }

    /// <summary>The next 32 bits without consuming them: enough for any prefix code, whose longest is 15.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint Peek()
    {
        if (_bits < 32)
            Fill();
        return (uint)_buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Skip(int count)
    {
        _buffer >>= count;
        _bits -= count;
        _consumed += count;
    }

    private void Fill()
    {
        if (_position + 8 <= _length)
        {
            // Take whole bytes up to 56 bits held; the bytes read past that are read again next time.
            var take = (63 - _bits) >> 3;
            _buffer |= BinaryPrimitives.ReadUInt64LittleEndian(new ReadOnlySpan<byte>(_data + _position, 8)) << _bits;
            _position += take;
            _bits += take * 8;
            return;
        }

        while (_bits <= 56)
        {
            if (_position < _length)
                _buffer |= (ulong)_data[_position++] << _bits;
            _bits += 8;
        }
    }
}
