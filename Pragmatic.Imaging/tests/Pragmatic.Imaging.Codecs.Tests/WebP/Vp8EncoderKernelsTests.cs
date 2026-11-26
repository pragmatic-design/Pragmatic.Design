using Pragmatic.Imaging.Codecs.WebP;

namespace Pragmatic.Imaging.Codecs.Tests.WebP;

/// <summary>
///     The lossy encoder's vectorised kernels compute exactly what their scalar forms compute, and its rate
///     estimate is what coding the tokens costs: on random blocks, every kind, context and starting position.
///     The scalar forms are what a machine without the vector instructions runs, and the CI machines never do.
/// </summary>
public sealed class Vp8EncoderKernelsTests
{
    private const int S = Vp8Prediction.Stride;

    [Fact]
    public void Dct_VectorAndScalar_GiveTheSameCoefficients()
    {
        var random = new Random(853);
        var source = new byte[S * 17];
        var prediction = new byte[S * 17];
        for (var trial = 0; trial < 2000; trial++)
        {
            random.NextBytes(source);
            random.NextBytes(prediction);
            var offset = Vp8Borders.Origin + (trial & 3) * 4 + ((trial >> 2) & 3) * 4 * S;
            var scalar = new short[16];
            var vector = new short[16];

            Vp8ForwardTransform.DctScalar(source, prediction, offset, scalar);
            Vp8ForwardTransform.DctVector(source, prediction, offset, vector);

            Assert.Equal(scalar, vector);

            if (!System.Runtime.Intrinsics.X86.Sse2.IsSupported)
                continue;
            var sse2 = new short[16];
            Vp8ForwardTransform.DctSse2(source, prediction, offset, sse2);
            Assert.Equal(scalar, sse2);
        }
    }

    [Fact]
    public void AddInverseDct_VectorAndScalar_GiveTheSamePixels()
    {
        var random = new Random(853);
        for (var trial = 0; trial < 2000; trial++)
        {
            var coefficients = new short[16];

            // Up to the 16-bit form's bound, well inside it, past it where 16 bits would overflow, and far past it.
            var range = (trial % 4) switch { 0 => 2049, 1 => 200, 2 => 8000, _ => 30000 };
            for (var i = 0; i < 16; i++)
                coefficients[i] = (short)random.Next(-range, range);
            var scalar = new byte[S * 17];
            random.NextBytes(scalar);
            var vector = (byte[])scalar.Clone();
            var sse2 = (byte[])scalar.Clone();

            Vp8InverseTransform.AddInverseDctScalar(coefficients, scalar, Vp8Borders.Origin, S);
            Vp8InverseTransform.AddInverseDctVector(coefficients, vector, Vp8Borders.Origin, S);

            Assert.Equal(scalar, vector);
            if (!System.Runtime.Intrinsics.X86.Sse2.IsSupported)
                continue;
            Vp8InverseTransform.AddInverseDctSse2(coefficients, sse2, Vp8Borders.Origin, S);
            Assert.Equal(scalar, sse2);
        }
    }

    [Fact]
    public void Transforms_WithThePredictionAsRowWords_AreTheTransformsOfThePredictionInABuffer()
    {
        if (!System.Runtime.Intrinsics.X86.Sse2.IsSupported)
            return;
        var random = new Random(853);
        var source = new byte[S * 17];
        var prediction = new byte[S * 17];
        var rows = new uint[4];
        for (var trial = 0; trial < 2000; trial++)
        {
            random.NextBytes(source);
            random.NextBytes(prediction);
            var offset = Vp8Borders.Origin + (trial & 3) * 4 + ((trial >> 2) & 3) * 4 * S;
            for (var r = 0; r < 4; r++)
                rows[r] = BitConverter.ToUInt32(prediction, offset + r * S);

            var fromBuffer = new short[16];
            var fromRows = new short[16];
            Vp8ForwardTransform.DctSse2(source, prediction, offset, fromBuffer);
            Vp8ForwardTransform.DctSse2(source, offset, rows, fromRows);
            Assert.Equal(fromBuffer, fromRows);

            // Past the 16-bit bound too, where both hand over to 32 bits.
            var range = (trial % 3) switch { 0 => 2049, 1 => 200, _ => 8000 };
            var coefficients = new short[16];
            for (var i = 0; i < 16; i++)
                coefficients[i] = (short)random.Next(-range, range);
            // The row-word form must write the whole block from the rows: its block starts as noise.
            var inPlace = (byte[])prediction.Clone();
            var written = new byte[S * 17];
            random.NextBytes(written);
            CopyOutsideBlock(prediction, written, offset);
            Vp8InverseTransform.AddInverseDctSse2(coefficients, inPlace, offset, S);
            Vp8InverseTransform.AddInverseDctSse2(coefficients, rows, written, offset, S);
            Assert.Equal(inPlace, written);
        }
    }

    /// <summary>Everything but the 4 x 4 block at <paramref name="offset" />, which is left as it is.</summary>
    private static void CopyOutsideBlock(byte[] from, byte[] to, int offset)
    {
        var block = new byte[16];
        for (var r = 0; r < 4; r++)
            Array.Copy(to, offset + r * S, block, r * 4, 4);
        from.CopyTo(to, 0);
        for (var r = 0; r < 4; r++)
            Array.Copy(block, r * 4, to, offset + r * S, 4);
    }

    [Fact]
    public void Subblocks_OfRandomNeighbours_AreEachModesSubblockPrediction()
    {
        var random = new Random(853);
        var single = new byte[S * 17];
        var rows = new uint[40];
        for (var trial = 0; trial < 2000; trial++)
        {
            random.NextBytes(single);
            var all = (byte[])single.Clone();
            var offset = Vp8Borders.Origin + (trial & 3) * 4 + ((trial >> 2) & 3) * 4 * S;
            Vp8Prediction.Subblocks(all, offset, rows);
            for (var mode = 0; mode < 10; mode++)
            {
                Vp8Prediction.Subblock(single, offset, mode);
                Vp8Prediction.Put(all, offset, rows.AsSpan(mode * 4, 4));
                Assert.Equal(single, all);
            }
        }
    }

    [Fact]
    public void TrueMotion4_VectorAndScalar_GiveTheSamePrediction()
    {
        var random = new Random(853);
        var scalar = new byte[S * 17];
        for (var trial = 0; trial < 2000; trial++)
        {
            // Neighbours far apart from the corner, so the clamping at 0 and 255 is reached too.
            random.NextBytes(scalar);
            var vector = (byte[])scalar.Clone();
            var offset = Vp8Borders.Origin + (trial & 3) * 4 + ((trial >> 2) & 3) * 4 * S;

            Vp8Prediction.TrueMotion4Scalar(ref scalar[offset]);
            Vp8Prediction.TrueMotion4Vector(ref vector[offset]);

            Assert.Equal(scalar, vector);
        }
    }

    [Fact]
    public void Quantize_VectorAndScalar_GiveTheSameLevelsAndCoefficients()
    {
        var random = new Random(853);
        for (var trial = 0; trial < 20000; trial++)
        {
            var matrix = new Vp8QuantMatrix(random.Next(4, 158), random.Next(4, 285), trial % 3);
            var scalar = new short[16];
            var range = 1 + trial % 3000;
            for (var i = 0; i < 16; i++)
                scalar[i] = (short)random.Next(-range, range);
            var vector = (short[])scalar.Clone();
            var avx2 = (short[])scalar.Clone();
            var scalarLevels = new short[16];
            var vectorLevels = new short[16];
            var avx2Levels = new short[16];
            var first = trial % 2;

            var scalarNonZero = matrix.QuantizeScalar(scalar, scalarLevels, first);
            var vectorNonZero = matrix.QuantizeVector(vector, vectorLevels, first);

            Assert.Equal(scalarNonZero, vectorNonZero);
            Assert.Equal(scalarLevels, vectorLevels);
            Assert.Equal(scalar, vector);

            // Where the 16-bit path can run: the x64 CI machines all can.
            if (!System.Runtime.Intrinsics.X86.Avx2.IsSupported)
                continue;
            var avx2NonZero = matrix.QuantizeAvx2(avx2, avx2Levels, first);
            Assert.Equal(scalarNonZero, avx2NonZero);
            Assert.Equal(scalarLevels, avx2Levels);
            Assert.Equal(scalar, avx2);
        }
    }

    [Fact]
    public void SumOfSquares_OfEveryBlockSize_IsTheSumOfSquaredDifferences()
    {
        var random = new Random(853);
        var a = new byte[S * 17];
        var b = new byte[S * 17];
        for (var trial = 0; trial < 500; trial++)
        {
            random.NextBytes(a);
            random.NextBytes(b);

            Assert.Equal(Squares(a, b, Vp8Borders.Origin, 4), Vp8Distortion.SumOfSquares4(a, b, Vp8Borders.Origin));
            Assert.Equal(Squares(a, b, Vp8Borders.Origin, 8), Vp8Distortion.SumOfSquares(a, b, Vp8Borders.Origin, 8, 8));
            Assert.Equal(Squares(a, b, Vp8Borders.Origin, 16), Vp8Distortion.SumOfSquares(a, b, Vp8Borders.Origin, 16, 16));
        }
    }

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

    private static int Squares(byte[] a, byte[] b, int offset, int size)
    {
        var sum = 0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var d = a[offset + y * S + x] - b[offset + y * S + x];
                sum += d * d;
            }
        }

        return sum;
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
