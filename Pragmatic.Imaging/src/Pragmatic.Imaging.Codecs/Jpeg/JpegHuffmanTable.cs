namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     One Huffman table from DHT, in the two forms a decoder needs: a 9-bit lookahead table that
///     answers most codes in one step, and the canonical maxcode/valptr arrays for the longer ones.
/// </summary>
internal sealed class JpegHuffmanTable
{
    /// <summary>
    ///     Twelve bits of lookahead: 8 KB per table, in L1, and all but the rarest codes of a fitted or
    ///     standard table resolved in one step. At nine bits a quality-90 file sent a large share of its AC
    ///     symbols down the bit-by-bit path.
    /// </summary>
    public const int LookupBits = 12;

    private readonly byte[] _values;
    private readonly int[] _maxCode = new int[18];
    private readonly int[] _valueOffset = new int[17];

    /// <summary>For each prefix of <see cref="LookupBits" /> bits: (code length &lt;&lt; 8) | symbol, or 0 when the code is longer.</summary>
    public ushort[] Lookup { get; } = new ushort[1 << LookupBits];

    /// <summary>
    ///     For an AC table: one entry per prefix of <see cref="LookupBits" /> bits, taking the coefficient as
    ///     far as the lookahead allows, so the sequential decoder reads one table per coefficient. Bits 0..7:
    ///     the bits consumed, a count a shift can take as it is. Negative: the code and its magnitude bits both
    ///     fit; bits 8..11 hold the run and bits 16..27 the value, in two's complement. Positive: the code fits
    ///     and bits 8..15 hold its symbol, the magnitude still to be read. Zero: the code is longer than the
    ///     lookahead.
    /// </summary>
    /// <remarks>
    ///     The count takes the low byte alone, with no flag beside it, so the decoder can read it as a byte and
    ///     shift by the raw entry: two expressions the JIT keeps apart, and the mask stays off the shift's path.
    /// </remarks>
    public int[] AcEntries { get; } = new int[1 << LookupBits];

    /// <summary>The bit of an <see cref="AcEntries" /> entry that says the value is in it: the sign.</summary>
    public const int AcEntryHasValue = unchecked((int)0x80000000);

    public JpegHuffmanTable(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values)
    {
        if (counts.Length != 16)
            throw new ImageDecodeException("JPEG Huffman table does not have 16 code-length counts.");
        _values = values.ToArray();

        var code = 0;
        var k = 0;
        for (var length = 1; length <= 16; length++)
        {
            var count = counts[length - 1];
            _valueOffset[length] = k - code;
            if (count == 0)
            {
                _maxCode[length] = -1;
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    if (k >= _values.Length)
                        throw new ImageDecodeException("JPEG Huffman table declares more codes than it has symbols.");
                    if (length <= LookupBits)
                    {
                        var shift = LookupBits - length;
                        var first = code << shift;
                        var entry = (ushort)((length << 8) | _values[k]);
                        for (var j = 0; j < (1 << shift); j++)
                            Lookup[first + j] = entry;
                    }

                    code++;
                    k++;
                }

                _maxCode[length] = code - 1;
            }

            code <<= 1;
            if (code > (1 << (length + 1)))
                throw new ImageDecodeException("JPEG Huffman table is not a valid prefix code.");
        }

        _maxCode[17] = int.MaxValue;
        BuildAcEntries();
    }

    /// <summary>
    ///     Reads, for every prefix, the symbol the lookup gives and, when they fit as well, the magnitude
    ///     bits after its code, already sign-extended: the decoder's common case is one read per coefficient.
    /// </summary>
    private void BuildAcEntries()
    {
        for (var prefix = 0; prefix < (1 << LookupBits); prefix++)
        {
            var entry = Lookup[prefix];
            if (entry == 0)
                continue;
            var length = entry >> 8;
            var symbol = entry & 0xFF;
            var run = symbol >> 4;
            var size = symbol & 15;
            if (size == 0 || length + size > LookupBits)
            {
                AcEntries[prefix] = (symbol << 8) | length;
                continue;
            }

            var bits = (prefix >> (LookupBits - length - size)) & ((1 << size) - 1);
            var negative = (bits - (1 << (size - 1))) >> 31;
            var value = bits + (negative & ((-1 << size) + 1)); // at most 10 bits and a sign: 12 hold it
            AcEntries[prefix] = ((value & 0xFFF) << 16) | (run << 8) | AcEntryHasValue | (length + size);
        }
    }

    /// <summary>The symbol of a code longer than the lookahead: <paramref name="code" /> is its first <see cref="LookupBits" /> bits, more are read one at a time.</summary>
    public byte DecodeSlow(ref JpegBitReader reader, int code)
    {
        var length = LookupBits;
        reader.EnsureBits(16 - LookupBits);
        while (code > _maxCode[length])
        {
            code = (code << 1) | reader.ReadBitsUnchecked(1);
            length++;
            if (length > 16)
                throw new ImageDecodeException("JPEG entropy-coded data holds a Huffman code the table does not define.");
        }

        var index = code + _valueOffset[length];
        if ((uint)index >= (uint)_values.Length)
            throw new ImageDecodeException("JPEG entropy-coded data holds a Huffman code the table does not define.");
        return _values[index];
    }
}
