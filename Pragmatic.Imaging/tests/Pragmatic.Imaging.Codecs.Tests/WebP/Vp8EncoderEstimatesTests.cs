using Pragmatic.Imaging.Codecs.WebP;

namespace Pragmatic.Imaging.Codecs.Tests.WebP;

/// <summary>
///     The lossy encoder's estimates are what they stand for: its rate is what coding the tokens costs, node by
///     node as the decoder reads them, and its susceptibility is libwebp's reach of a histogram against its peak.
///     On random inputs, every kind, context and starting position.
/// </summary>
public sealed class Vp8EncoderEstimatesTests
{
    [Fact]
    public void Residual_OfRandomBlocks_IsTheCostOfWritingTheirTokens()
    {
        var random = new Random(853);
        var probabilities = Vp8Tables.DefaultCoefficientProbabilities.ToArray();
        for (var i = 0; i < probabilities.Length; i++)
            probabilities[i] = (byte)random.Next(1, 256);
        using var costs = new Vp8Costs();
        costs.Update(probabilities);

        for (var trial = 0; trial < 20000; trial++)
        {
            var type = trial % 4;
            var first = type == 0 ? 1 : 0;
            var ctx = (trial / 4) % 3;
            var levels = new short[16];
            var count = random.Next(0, 17);
            for (var n = first; n < 16; n++)
            {
                if (random.Next(16) < count)
                    levels[n] = (short)(random.Next(2) == 0 ? random.Next(-3, 4) : random.Next(-2047, 2048));
            }

            Assert.Equal(TokenCost(probabilities, type, first, ctx, levels), costs.Residual(type, first, ctx, levels, out var coded));
            Assert.Equal(levels.AsSpan(first).ContainsAnyExcept((short)0), coded);
        }
    }

    [Fact]
    public void HistogramAlpha_OfRandomHistograms_IsTheReachAgainstThePeakOfTheirSum()
    {
        var random = new Random(853);
        var histogram = new int[4 * 32];
        for (var trial = 0; trial < 20000; trial++)
        {
            // Empty, only the first bin, a single count, and sparse spreads of every reach.
            var reach = trial % 4 == 0 ? 0 : random.Next(1, 33);
            var density = random.Next(1, 8);
            for (var i = 0; i < histogram.Length; i++)
                histogram[i] = i % 32 < reach && random.Next(density) == 0 ? random.Next(0, 40) : 0;
            if (trial % 97 == 0)
            {
                Array.Clear(histogram);
                histogram[random.Next(4) * 32 + random.Next(32)] = 1;
            }

            Assert.Equal(ReachAgainstPeak(histogram), Vp8Analysis.HistogramAlpha(histogram));
        }
    }

    /// <summary>libwebp's <c>GetAlpha</c> over the sum of the four histograms, a bin at a time.</summary>
    private static int ReachAgainstPeak(int[] histogram)
    {
        int max = 0, lastNonZero = 1;
        for (var k = 0; k < 32; k++)
        {
            var count = histogram[k] + histogram[32 + k] + histogram[64 + k] + histogram[96 + k];
            if (count <= 0)
                continue;
            max = Math.Max(max, count);
            lastNonZero = k;
        }

        return max > 1 ? 2 * 255 * lastNonZero / max : 0;
    }

    /// <summary>
    ///     The cost of the bits the token tree codes for a block (RFC 6386, section 13.2), node by node, as the
    ///     decoder reads them: not the encoder's tables.
    /// </summary>
    private static int TokenCost(byte[] probabilities, int type, int first, int ctx, short[] levels)
    {
        var last = -1;
        for (var n = 15; n >= first; n--)
        {
            if (levels[n] != 0)
            {
                last = n;
                break;
            }
        }

        var bands = Vp8Tables.Bands;
        var p = Offset(type, bands[first], ctx);
        var cost = Vp8Costs.Bit(last >= 0 ? 1 : 0, probabilities[p]);
        if (last < 0)
            return cost;

        for (var n = first; n <= last; n++)
        {
            var v = Math.Abs((int)levels[n]);
            cost += Vp8Costs.Bit(v != 0 ? 1 : 0, probabilities[p + 1]);
            if (v == 0)
            {
                p = Offset(type, bands[n + 1], 0);
                continue;
            }

            cost += Vp8Costs.Bit(v > 1 ? 1 : 0, probabilities[p + 2]);
            if (v > 1)
                cost += LargeCost(probabilities, p, v);
            cost += 256; // the sign
            p = Offset(type, bands[n + 1], v == 1 ? 1 : 2);
            if (n < 15)
                cost += Vp8Costs.Bit(n < last ? 1 : 0, probabilities[p]);
        }

        return cost;
    }

    private static int LargeCost(byte[] probabilities, int p, int v)
    {
        int Bit(int bit, int probability) => Vp8Costs.Bit(bit, probability);
        if (v <= 4)
            return Bit(0, probabilities[p + 3]) + Bit(v != 2 ? 1 : 0, probabilities[p + 4]) + (v != 2 ? Bit(v == 4 ? 1 : 0, probabilities[p + 5]) : 0);
        var cost = Bit(1, probabilities[p + 3]);
        if (v <= 10)
        {
            cost += Bit(0, probabilities[p + 6]);
            return v <= 6
                ? cost + Bit(0, probabilities[p + 7]) + Bit(v == 6 ? 1 : 0, 159)
                : cost + Bit(1, probabilities[p + 7]) + Bit(v >= 9 ? 1 : 0, 165) + Bit((v & 1) == 0 ? 1 : 0, 145);
        }

        cost += Bit(1, probabilities[p + 6]);
        var (category, extra, bits) = v < 19 ? (0, new byte[] { 173, 148, 140 }, 3)
            : v < 35 ? (1, [176, 155, 140, 135], 4)
            : v < 67 ? (2, [180, 157, 141, 134, 130], 5)
            : (3, [254, 254, 243, 230, 196, 177, 153, 140, 133, 130, 129], 11);
        cost += Bit(category >> 1, probabilities[p + 8]) + Bit(category & 1, probabilities[p + 9 + (category >> 1)]);
        var rest = v - (3 + (8 << category));
        for (var i = 0; i < bits; i++)
            cost += Bit((rest >> (bits - 1 - i)) & 1, extra[i]);
        return cost;
    }

    private static int Offset(int type, int band, int ctx) =>
        ((type * Vp8Tables.NumBands + band) * Vp8Tables.NumContexts + ctx) * Vp8Tables.NumProbabilities;
}
