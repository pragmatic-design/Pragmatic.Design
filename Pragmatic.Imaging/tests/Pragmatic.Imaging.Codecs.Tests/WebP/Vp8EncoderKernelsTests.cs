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

            Assert.Equal(TokenCost(probabilities, type, first, ctx, levels), costs.Residual(type, first, ctx, levels));
        }
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
