namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     Reads one prefix code from a lossless stream, in either of its two forms (RFC 9649, section 3.7.2.1):
///     a simple code of one or two symbols, or code lengths themselves coded with a code over 19 symbols.
/// </summary>
internal static class Vp8LCodeReader
{
    private const int CodeLengthCodes = 19;
    private const int DefaultRepeatLength = 8;

    private static ReadOnlySpan<byte> CodeLengthOrder => [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    public static Vp8LHuffmanTable Read(Vp8LBitReader reader, int alphabetSize)
    {
        var lengths = new int[alphabetSize];
        if (reader.ReadBits(1) == 1)
        {
            var symbols = (int)reader.ReadBits(1) + 1;
            var first = (int)reader.ReadBits(reader.ReadBits(1) == 1 ? 8 : 1);
            Set(lengths, first);
            if (symbols == 2)
                Set(lengths, (int)reader.ReadBits(8));
            return Vp8LHuffmanTable.Build(lengths);
        }

        Span<int> codeLengthLengths = stackalloc int[CodeLengthCodes];
        var count = 4 + (int)reader.ReadBits(4);
        for (var i = 0; i < count; i++)
            codeLengthLengths[CodeLengthOrder[i]] = (int)reader.ReadBits(3);
        var codeLengthCode = Vp8LHuffmanTable.Build(codeLengthLengths);

        var maxSymbol = alphabetSize;
        if (reader.ReadBits(1) == 1)
        {
            var bits = 2 + 2 * (int)reader.ReadBits(3);
            maxSymbol = 2 + (int)reader.ReadBits(bits);
            if (maxSymbol > alphabetSize)
                throw new ImageDecodeException("WebP: a prefix code declares more symbols than its alphabet.");
        }

        var previous = DefaultRepeatLength;
        var symbol = 0;
        while (symbol < alphabetSize)
        {
            if (maxSymbol-- == 0)
                break;
            var code = codeLengthCode.ReadSymbol(reader);
            if (code < 16)
            {
                lengths[symbol++] = code;
                if (code != 0)
                    previous = code;
                continue;
            }

            var (extraBits, offset, value) = code switch
            {
                16 => (2, 3, previous),
                17 => (3, 3, 0),
                _ => (7, 11, 0),
            };
            var repeat = offset + (int)reader.ReadBits(extraBits);
            if (symbol + repeat > alphabetSize)
                throw new ImageDecodeException("WebP: a run of code lengths overflows the alphabet.");
            lengths.AsSpan(symbol, repeat).Fill(value);
            symbol += repeat;
        }

        return Vp8LHuffmanTable.Build(lengths);
    }

    private static void Set(int[] lengths, int symbol)
    {
        if (symbol >= lengths.Length)
            throw new ImageDecodeException("WebP: a simple prefix code names a symbol outside its alphabet.");
        lengths[symbol] = 1;
    }
}
