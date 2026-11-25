namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     Reads one prefix code from a lossless stream, in either of its two forms (RFC 9649, section 3.7.2.1):
///     a simple code of one or two symbols, or code lengths themselves coded with a code over 19 symbols.
/// </summary>
internal static class Vp8LCodeReader
{
    private const int CodeLengthCodes = 19;
    private const int DefaultRepeatLength = 8;

    /// <summary>The largest alphabet: 256 literals, 24 lengths and a colour cache of 2^11.</summary>
    public const int MaxAlphabet = 256 + 24 + (1 << 11);

    private static ReadOnlySpan<byte> CodeLengthOrder => [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    /// <summary>Reads a code over <paramref name="alphabetSize" /> symbols into <paramref name="codes" /> and returns its root.</summary>
    /// <param name="reader">The stream, at the code.</param>
    /// <param name="codes">The block the code is added to.</param>
    /// <param name="alphabetSize">How many symbols the code is over.</param>
    /// <param name="scratch">Two times <see cref="MaxAlphabet" /> of working space.</param>
    public static int Read(Vp8LBitReader reader, Vp8LHuffmanCodes codes, int alphabetSize, Span<int> scratch)
    {
        var lengths = scratch[..alphabetSize];
        var sorted = scratch.Slice(MaxAlphabet, alphabetSize);
        lengths.Clear();
        if (reader.ReadBits(1) == 1)
        {
            var symbols = (int)reader.ReadBits(1) + 1;
            var first = (int)reader.ReadBits(reader.ReadBits(1) == 1 ? 8 : 1);
            Set(lengths, first);
            if (symbols == 2)
                Set(lengths, (int)reader.ReadBits(8));
            return codes.Add(lengths, sorted);
        }

        Span<int> codeLengthLengths = stackalloc int[CodeLengthCodes];
        var count = 4 + (int)reader.ReadBits(4);
        for (var i = 0; i < count; i++)
            codeLengthLengths[CodeLengthOrder[i]] = (int)reader.ReadBits(3);
        Span<int> codeLengthSorted = stackalloc int[CodeLengthCodes];
        var codeLengthCode = codes.Add(codeLengthLengths, codeLengthSorted);

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
            var code = Vp8LHuffmanCodes.ReadSymbol(codes.Entries, codeLengthCode, reader);
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
            lengths.Slice(symbol, repeat).Fill(value);
            symbol += repeat;
        }

        return codes.Add(lengths, sorted);
    }

    private static void Set(Span<int> lengths, int symbol)
    {
        if (symbol >= lengths.Length)
            throw new ImageDecodeException("WebP: a simple prefix code names a symbol outside its alphabet.");
        lengths[symbol] = 1;
    }
}
