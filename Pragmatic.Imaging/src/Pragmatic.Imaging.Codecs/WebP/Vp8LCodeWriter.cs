namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     Writes one prefix code of the lossless format (RFC 9649, section 3.7.2.1): the simple form when one
///     or two symbols below 256 are used, otherwise the code lengths, themselves coded with a code over 19
///     symbols and shortened by runs (16 repeats the previous length, 17 and 18 repeat zero), as libwebp's
///     <c>VP8LCreateCompressedHuffmanTree</c> writes them.
/// </summary>
internal static class Vp8LCodeWriter
{
    private const int CodeLengthCodes = 19;

    private static ReadOnlySpan<byte> CodeLengthOrder => [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    /// <summary>
    ///     Builds the code for <paramref name="frequencies" />, writes it, and leaves in
    ///     <paramref name="lengths" /> and <paramref name="codes" /> what the symbols are then written with.
    /// </summary>
    public static void Write(Vp8LBitWriter writer, ReadOnlySpan<uint> frequencies, Span<byte> lengths, Span<ushort> codes)
    {
        var used = Vp8LHuffmanBuilder.Build(frequencies, 15, lengths, codes);
        Span<int> symbols = stackalloc int[2];
        var found = 0;
        for (var i = 0; i < frequencies.Length && found < 2; i++)
        {
            if (frequencies[i] != 0)
                symbols[found++] = i;
        }

        if (used == 0)
        {
            WriteSimple(writer, [0]);
            return;
        }

        if (used <= 2 && symbols[used - 1] < 256)
        {
            WriteSimple(writer, symbols[..used]);
            if (used == 2)
            {
                // The simple form makes the first symbol named the code 0 and the second the code 1.
                lengths[symbols[0]] = lengths[symbols[1]] = 1;
                codes[symbols[0]] = 0;
                codes[symbols[1]] = 1;
            }

            return;
        }

        if (used == 1)
        {
            // One symbol at or above 256 has no simple form: it is written as a code whose only length is
            // 1, which the decoder reads as a code that takes no bits.
            lengths[symbols[0]] = 1;
            WriteNormal(writer, lengths);
            lengths[symbols[0]] = 0;
            return;
        }

        WriteNormal(writer, lengths);
    }

    private static void WriteSimple(Vp8LBitWriter writer, ReadOnlySpan<int> symbols)
    {
        writer.Write(1, 1);
        writer.Write((uint)symbols.Length - 1, 1);
        if (symbols[0] <= 1)
        {
            writer.Write(0, 1);
            writer.Write((uint)symbols[0], 1);
        }
        else
        {
            writer.Write(1, 1);
            writer.Write((uint)symbols[0], 8);
        }

        if (symbols.Length == 2)
            writer.Write((uint)symbols[1], 8);
    }

    private static void WriteNormal(Vp8LBitWriter writer, ReadOnlySpan<byte> lengths)
    {
        // The lengths as tokens: 0..15 themselves, 16 to 18 for runs, each with its extra bits.
        Span<byte> tokens = stackalloc byte[lengths.Length];
        Span<byte> extras = stackalloc byte[lengths.Length];
        var count = Tokenize(lengths, tokens, extras);

        Span<uint> frequencies = stackalloc uint[CodeLengthCodes];
        for (var i = 0; i < count; i++)
            frequencies[tokens[i]]++;
        Span<byte> codeLengthLengths = stackalloc byte[CodeLengthCodes];
        Span<ushort> codeLengthCodes = stackalloc ushort[CodeLengthCodes];
        var used = Vp8LHuffmanBuilder.Build(frequencies, 7, codeLengthLengths, codeLengthCodes);
        if (used == 1)
        {
            // One token kind: give it length 1, a code the decoder reads with no bits.
            for (var i = 0; i < CodeLengthCodes; i++)
            {
                if (frequencies[i] != 0)
                    codeLengthLengths[i] = 1;
            }
        }

        var written = CodeLengthCodes;
        while (written > 4 && codeLengthLengths[CodeLengthOrder[written - 1]] == 0)
            written--;

        writer.Write(0, 1); // a normal code
        writer.Write((uint)written - 4, 4);
        for (var i = 0; i < written; i++)
            writer.Write(codeLengthLengths[CodeLengthOrder[i]], 3);
        writer.Write(0, 1); // the lengths cover the whole alphabet

        for (var i = 0; i < count; i++)
        {
            var token = tokens[i];
            if (used > 1)
                writer.Write(codeLengthCodes[token], codeLengthLengths[token]);
            switch (token)
            {
                case 16:
                    writer.Write(extras[i], 2);
                    break;
                case 17:
                    writer.Write(extras[i], 3);
                    break;
                case 18:
                    writer.Write(extras[i], 7);
                    break;
            }
        }
    }

    private static int Tokenize(ReadOnlySpan<byte> lengths, Span<byte> tokens, Span<byte> extras)
    {
        var count = 0;
        var previous = 8; // the decoder's starting value for a run of 16
        var i = 0;
        while (i < lengths.Length)
        {
            var value = lengths[i];
            var k = i + 1;
            while (k < lengths.Length && lengths[k] == value)
                k++;
            var runs = k - i;
            if (value == 0)
            {
                while (runs > 0)
                {
                    if (runs < 3)
                    {
                        for (; runs > 0; runs--)
                            Emit(tokens, extras, ref count, 0, 0);
                    }
                    else if (runs < 11)
                    {
                        Emit(tokens, extras, ref count, 17, runs - 3);
                        runs = 0;
                    }
                    else
                    {
                        var take = Math.Min(runs, 138);
                        Emit(tokens, extras, ref count, 18, take - 11);
                        runs -= take;
                    }
                }
            }
            else
            {
                if (value != previous)
                {
                    Emit(tokens, extras, ref count, value, 0);
                    runs--;
                }

                while (runs > 0)
                {
                    if (runs < 3)
                    {
                        for (; runs > 0; runs--)
                            Emit(tokens, extras, ref count, value, 0);
                    }
                    else
                    {
                        var take = Math.Min(runs, 6);
                        Emit(tokens, extras, ref count, 16, take - 3);
                        runs -= take;
                    }
                }

                previous = value;
            }

            i = k;
        }

        return count;
    }

    private static void Emit(Span<byte> tokens, Span<byte> extras, ref int count, int token, int extra)
    {
        tokens[count] = (byte)token;
        extras[count] = (byte)extra;
        count++;
    }
}
