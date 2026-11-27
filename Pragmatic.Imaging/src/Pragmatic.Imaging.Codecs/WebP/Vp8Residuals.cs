// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The DCT coefficients of one macroblock, read from its token partition (RFC 6386, section 13) the way
///     libwebp's <c>ParseResiduals</c> reads them: the Y2 block first when the luma is predicted whole, then
///     the 16 luma blocks, then 4 for U and 4 for V. Coefficients come out dequantized, in raster order, 16
///     per block; the Y2 block is inverted at once into the DC of each luma block.
/// </summary>
internal static class Vp8Residuals
{
    private const int TypeLumaAfterY2 = 0;
    private const int TypeY2 = 1;
    private const int TypeChroma = 2;
    private const int TypeLumaWhole = 3;

    /// <summary>
    ///     Reads the coefficients into <paramref name="coefficients" /> (384 values, cleared by the caller) and
    ///     returns, per block, whether it has anything to add: bit <c>b</c> for luma block <c>b</c>, bits 16 to
    ///     19 for U, 20 to 23 for V.
    /// </summary>
    /// <param name="reader">The token partition of this macroblock's row.</param>
    /// <param name="probabilities">The coefficient probabilities of the frame.</param>
    /// <param name="quantizer">The dequantization steps of the macroblock's segment.</param>
    /// <param name="hasY2">Whether the luma is predicted whole, so its DC comes in a Y2 block.</param>
    /// <param name="coefficients">Where the 384 coefficients go, cleared by the caller.</param>
    /// <param name="above">
    ///     The non-zero flags along the top of this macroblock, updated for the one below: four luma
    ///     columns, two U, two V, then Y2 (<see cref="ContextCount" /> in all).
    /// </param>
    /// <param name="left">The same flags along its left side, updated for the one to its right.</param>
    public static int Read(Vp8BoolDecoder reader, byte[] probabilities, Vp8Quantizer quantizer, bool hasY2,
        Span<byte> above, Span<byte> left, Span<short> coefficients)
    {
        var nonZero = 0;
        var first = 0;
        var lumaType = TypeLumaWhole;
        if (hasY2)
        {
            Span<short> y2 = stackalloc short[16];
            var count = ReadBlock(reader, probabilities, TypeY2, above[Y2] + left[Y2], quantizer.Y2Dc, quantizer.Y2Ac, 0, y2);
            above[Y2] = left[Y2] = count > 0 ? (byte)1 : (byte)0;
            Vp8InverseTransform.Walsh(y2, coefficients);
            first = 1;
            lumaType = TypeLumaAfterY2;
        }

        for (var y = 0; y < 4; y++)
        {
            int flag = left[y];
            for (var x = 0; x < 4; x++)
            {
                var block = y * 4 + x;
                var output = coefficients.Slice(block * 16, 16);
                var count = ReadBlock(reader, probabilities, lumaType, flag + above[x], quantizer.YDc, quantizer.YAc, first, output);
                flag = count > first ? 1 : 0;
                above[x] = (byte)flag;
                if (count > 1 || output[0] != 0)
                    nonZero |= 1 << block;
            }

            left[y] = (byte)flag;
        }

        for (var plane = 0; plane < 2; plane++)
        {
            var offset = 4 + plane * 2;
            for (var y = 0; y < 2; y++)
            {
                int flag = left[offset + y];
                for (var x = 0; x < 2; x++)
                {
                    var block = 16 + plane * 4 + y * 2 + x;
                    var output = coefficients.Slice(block * 16, 16);
                    var count = ReadBlock(reader, probabilities, TypeChroma, flag + above[offset + x], quantizer.UvDc, quantizer.UvAc, 0, output);
                    flag = count > 0 ? 1 : 0;
                    above[offset + x] = (byte)flag;
                    if (count > 1 || output[0] != 0)
                        nonZero |= 1 << block;
                }

                left[offset + y] = (byte)flag;
            }
        }

        return nonZero;
    }

    /// <summary>How many non-zero flags a macroblock keeps on each side.</summary>
    public const int ContextCount = 9;

    private const int Y2 = 8;

    /// <summary>
    ///     Clears the flags of a macroblock with no coefficients: luma and chroma always, Y2 only when the
    ///     macroblock has a Y2 block, as libwebp does.
    /// </summary>
    public static void Skip(Span<byte> above, Span<byte> left, bool hasY2)
    {
        above[..8].Clear();
        left[..8].Clear();
        if (hasY2)
            above[Y2] = left[Y2] = 0;
    }

    /// <summary>
    ///     One block's tokens from position <paramref name="n" /> on; returns the position after the last
    ///     non-zero coefficient (libwebp's <c>GetCoeffs</c>).
    /// </summary>
    private static int ReadBlock(Vp8BoolDecoder reader, byte[] probabilities, int type, int context, int dc, int ac, int n,
        Span<short> output)
    {
        var bands = Vp8Tables.Bands;
        var zigzag = Vp8Tables.Zigzag;
        var p = Offset(type, bands[n], context);
        for (; n < 16; n++)
        {
            if (reader.ReadBit(probabilities[p]) == 0)
                return n;
            while (reader.ReadBit(probabilities[p + 1]) == 0)
            {
                p = Offset(type, bands[++n], 0);
                if (n == 16)
                    return 16;
            }

            int value;
            if (reader.ReadBit(probabilities[p + 2]) == 0)
            {
                value = 1;
                p = Offset(type, bands[n + 1], 1);
            }
            else
            {
                value = LargeValue(reader, probabilities, p);
                p = Offset(type, bands[n + 1], 2);
            }

            if (reader.ReadBit(128) == 1)
                value = -value;
            output[zigzag[n]] = (short)(value * (n > 0 ? ac : dc));
        }

        return 16;
    }

    private static int LargeValue(Vp8BoolDecoder reader, byte[] probabilities, int p)
    {
        if (reader.ReadBit(probabilities[p + 3]) == 0)
            return reader.ReadBit(probabilities[p + 4]) == 0 ? 2 : 3 + reader.ReadBit(probabilities[p + 5]);

        if (reader.ReadBit(probabilities[p + 6]) == 0)
        {
            if (reader.ReadBit(probabilities[p + 7]) == 0)
                return 5 + reader.ReadBit(159);
            var v = 7 + 2 * reader.ReadBit(165);
            return v + reader.ReadBit(145);
        }

        var bit1 = reader.ReadBit(probabilities[p + 8]);
        var bit0 = reader.ReadBit(probabilities[p + 9 + bit1]);
        var category = 2 * bit1 + bit0;
        var table = category switch
        {
            0 => Vp8Tables.Category3,
            1 => Vp8Tables.Category4,
            2 => Vp8Tables.Category5,
            _ => Vp8Tables.Category6,
        };
        var value = 0;
        for (var i = 0; table[i] != 0; i++)
            value += value + reader.ReadBit(table[i]);
        return value + 3 + (8 << category);
    }

    private static int Offset(int type, int band, int context) =>
        ((type * Vp8Tables.NumBands + band) * Vp8Tables.NumContexts + context) * Vp8Tables.NumProbabilities;
}
