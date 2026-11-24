namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>A Huffman table as the encoder uses it: the code and its length for each symbol, plus what DHT must carry.</summary>
internal sealed class JpegEncodingTable
{
    private JpegEncodingTable(byte[] counts, byte[] symbols, ushort[] codes, byte[] lengths)
    {
        Counts = counts;
        Symbols = symbols;
        Codes = codes;
        Lengths = lengths;
    }

    /// <summary>Codes per length, 1..16: the first 16 bytes of the DHT entry.</summary>
    public byte[] Counts { get; }

    /// <summary>Symbols in code order: the rest of the DHT entry.</summary>
    public byte[] Symbols { get; }

    public ushort[] Codes { get; }

    /// <summary>Code length per symbol; zero for a symbol the table does not have.</summary>
    public byte[] Lengths { get; }

    public static JpegEncodingTable FromCanonical(byte[] counts, byte[] symbols)
    {
        var codes = new ushort[256];
        var lengths = new byte[256];
        var code = 0;
        var k = 0;
        for (var length = 1; length <= 16; length++)
        {
            for (var i = 0; i < counts[length - 1]; i++)
            {
                var symbol = symbols[k++];
                codes[symbol] = (ushort)code;
                lengths[symbol] = (byte)length;
                code++;
            }

            code <<= 1;
        }

        if (k != symbols.Length)
            throw new ArgumentException("The code-length counts do not add up to the number of symbols.", nameof(counts));
        return new JpegEncodingTable(counts, symbols, codes, lengths);
    }

    /// <summary>
    ///     The four tables of a scan as one array of 1,024 entries, table t at t·256: entry (t·256 + symbol)
    ///     holds the code in its low half and the code length in its high half. Table order: DC luma,
    ///     AC luma, DC chroma, AC chroma.
    /// </summary>
    public static void Flatten(JpegEncodingTable dcLuma, JpegEncodingTable acLuma, JpegEncodingTable dcChroma, JpegEncodingTable acChroma, Span<uint> codes)
    {
        FlattenInto(dcLuma, codes);
        FlattenInto(acLuma, codes[256..]);
        FlattenInto(dcChroma, codes[512..]);
        FlattenInto(acChroma, codes[768..]);
    }

    private static void FlattenInto(JpegEncodingTable table, Span<uint> codes)
    {
        for (var s = 0; s < 256; s++)
            codes[s] = table.Codes[s] | ((uint)table.Lengths[s] << 16);
    }
}
