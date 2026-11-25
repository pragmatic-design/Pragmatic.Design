using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     Bits of the entropy-coded segment, most significant first, with the byte stuffing undone: an
///     0xFF followed by 0x00 is one 0xFF byte; an 0xFF followed by anything else is a marker, where the
///     segment ends and from where the reader hands out zeros.
/// </summary>
/// <remarks>
///     The buffer is left-aligned: the next bit to read is bit 63, and every bit below the count is
///     zero. A peek is then one shift by a constant, and consuming is one shift left by a count that can
///     come straight out of a table entry, with nothing to subtract or mask on the way. The fill takes
///     eight bytes in one load when none of them is 0xFF, the common case, and one at a time otherwise.
///     The hot loop keeps the state in locals (<see cref="Export" />, <see cref="TryFillFast" />) and
///     comes back here only for the slow fill and the long codes.
/// </remarks>
internal ref struct JpegBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;
    private ulong _buffer;
    private int _bitCount;
    private bool _markerHit;

    public JpegBitReader(ReadOnlySpan<byte> data, int position)
    {
        _data = data;
        _position = position;
    }

    /// <summary>Where the next byte would be read from: after a scan, the marker that ended it or the end.</summary>
    public readonly int Position => _position;

    /// <summary>The whole segment the reader walks, for a hot loop that fills from it directly.</summary>
    public readonly ReadOnlySpan<byte> Data => _data;

    /// <summary>The bit buffer, its count and the read position, for a hot loop that keeps them in locals and writes them back.</summary>
    public readonly (ulong Buffer, int BitCount, int Position) Export() => (_buffer, _bitCount, _position);

    public void Import(ulong buffer, int bitCount, int position)
    {
        _buffer = buffer;
        _bitCount = bitCount;
        _position = position;
    }

    /// <summary>Makes at least <paramref name="count" /> bits (at most 32) available to the unchecked reads.</summary>
    public void EnsureBits(int count)
    {
        if (_bitCount < count)
            Fill();
    }

    /// <summary>The next <paramref name="count" /> bits, 1 to 32, left where they are.</summary>
    public readonly int PeekUnchecked(int count) => (int)(_buffer >> (64 - count));

    public void SkipUnchecked(int count)
    {
        _buffer <<= count;
        _bitCount -= count;
    }

    public int ReadBitsUnchecked(int count)
    {
        // Two shifts, so that a count of zero reads zero: one shift by 64 would read everything.
        var value = (int)((_buffer >> 1) >> (63 - count));
        _buffer <<= count;
        _bitCount -= count;
        return value;
    }

    public int ReadBit()
    {
        EnsureBits(1);
        return ReadBitsUnchecked(1);
    }

    public int ReadBits(int count)
    {
        if (count == 0)
            return 0;
        EnsureBits(count);
        return ReadBitsUnchecked(count);
    }

    /// <summary>A coefficient's magnitude bits, sign-extended the JPEG way. Unchecked: the caller ensured the bits.</summary>
    public int ReceiveUnchecked(int size)
    {
        if (size == 0)
            return 0;
        var value = ReadBitsUnchecked(size);
        var negative = (value - (1 << (size - 1))) >> 31;
        return value + (negative & ((-1 << size) + 1));
    }

    /// <summary>Decodes one symbol. Needs 16 bits ensured for the fast path; the slow path ensures its own.</summary>
    public byte DecodeHuffman(JpegHuffmanTable table)
    {
        var peek = PeekUnchecked(JpegHuffmanTable.LookupBits);
        var entry = table.Lookup[peek];
        if (entry != 0)
        {
            SkipUnchecked(entry >> 8);
            return (byte)entry;
        }

        SkipUnchecked(JpegHuffmanTable.LookupBits);
        return table.DecodeSlow(ref this, peek);
    }

    /// <summary>
    ///     At a restart: drops the bits left in the current byte, and steps over the RSTn marker that
    ///     must follow. Reports false when it is not there, for the caller to decide how much to trust the rest.
    /// </summary>
    public bool Restart()
    {
        // Bytes taken into the buffer but not consumed belong to the data after the marker only if we
        // overshot it; the fill never crosses a marker, so what is buffered is before it and is dropped.
        _buffer = 0;
        _bitCount = 0;
        _markerHit = false;
        if (_position + 1 < _data.Length && _data[_position] == 0xFF && _data[_position + 1] >= JpegMarker.Rst0 && _data[_position + 1] <= JpegMarker.Rst7)
        {
            _position += 2;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     The fast fill, on state held in locals: eight bytes in one load when the data has eight more and
    ///     none is 0xFF; the whole bytes that fit above the count go into the buffer, the rest stays for the
    ///     next fill. False when the slow fill is needed: near the end, at a stuffed byte or at a marker.
    ///     Pointers, so that a hot loop keeps two registers for the data (where it is, where it ends)
    ///     instead of three.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool TryFillFast(ref byte* cursor, byte* end, ref ulong buffer, ref int bitCount)
    {
        if (end - cursor < 8)
            return false;
        var word = BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<ulong>(cursor));
        if (HasByte0xFF(word))
            return false;

        // Only whole bytes go in: the bits of the partial one below them are masked off, so that the
        // bits under the count stay zero for the next fill to OR into.
        var free = 64 - bitCount;
        buffer |= (word >> bitCount) & (~0UL << (free & 7));
        var bytes = free >> 3;
        cursor += bytes;
        bitCount += bytes << 3;
        return true;
    }

    /// <summary>Tops the buffer up past 56 bits. Past a marker, the bits are zeros.</summary>
    private unsafe void Fill()
    {
        fixed (byte* start = _data)
        {
            var cursor = start + _position;
            if (TryFillFast(ref cursor, start + _data.Length, ref _buffer, ref _bitCount))
            {
                _position = (int)(cursor - start);
                return;
            }
        }

        while (_bitCount <= 56)
        {
            if (_markerHit || _position >= _data.Length)
            {
                _bitCount += 8; // zeros: the bits under the count already are
                continue;
            }

            var b = _data[_position];
            if (b == 0xFF)
            {
                var next = _position + 1 < _data.Length ? _data[_position + 1] : (byte)0xD9;
                if (next == 0x00)
                {
                    _position += 2;
                }
                else
                {
                    // A marker: the segment is over. Stay on it, so the caller finds it after the scan.
                    _markerHit = true;
                    continue;
                }
            }
            else
            {
                _position++;
            }

            _buffer |= (ulong)b << (56 - _bitCount);
            _bitCount += 8;
        }
    }

    /// <summary>True when any byte of <paramref name="word" /> is 0xFF: the zero-byte test on its complement.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasByte0xFF(ulong word)
    {
        var inverted = ~word;
        return ((inverted - 0x0101010101010101UL) & ~inverted & 0x8080808080808080UL) != 0;
    }
}
