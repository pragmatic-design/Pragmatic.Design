using Pragmatic.Imaging.Codecs.WebP;

namespace Pragmatic.Imaging.Codecs.Tests.WebP;

/// <summary>
///     The lossy encoder's vectorised kernels compute exactly what their scalar forms compute, on random blocks.
///     The scalar forms are what a machine without the vector instructions runs, and the CI machines never do.
///     Its estimates, the rate and the susceptibility, are <see cref="Vp8EncoderEstimatesTests" />.
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
    public void AddInverseDct2_IsThePredictionWithEachBlockAddedOnItsOwn()
    {
        var random = new Random(853);
        for (var trial = 0; trial < 4000; trial++)
        {
            // Each block within the 16-bit form's bound, or past it where 16 bits would overflow: alone or both.
            var coefficients = new short[32];
            for (var block = 0; block < 2; block++)
            {
                var range = ((trial >> block) % 4) switch { 0 => 2049, 1 => 200, 2 => 8000, _ => 30000 };
                for (var i = 0; i < 16; i++)
                    coefficients[block * 16 + i] = (short)random.Next(-range, range);
            }

            var prediction = new byte[S * 17];
            random.NextBytes(prediction);
            var offset = Vp8Borders.Origin + (trial & 1) * 8 + ((trial >> 1) & 3) * 4 * S;

            // Expected: the prediction's 8 x 4 pixels copied, then each block added in place by the scalar form;
            // everything outside them as the reconstruction held it.
            var reconstruction = new byte[S * 17];
            random.NextBytes(reconstruction);
            var expected = (byte[])reconstruction.Clone();
            for (var y = 0; y < 4; y++)
                Array.Copy(prediction, offset + y * S, expected, offset + y * S, 8);
            Vp8InverseTransform.AddInverseDctScalar(coefficients.AsSpan(0, 16), expected, offset, S);
            Vp8InverseTransform.AddInverseDctScalar(coefficients.AsSpan(16, 16), expected, offset + 4, S);

            Vp8InverseTransform.AddInverseDct2(coefficients, prediction, reconstruction, offset, S);
            Assert.Equal(expected, reconstruction);

            // And in place, the prediction its own reconstruction.
            var inPlace = (byte[])prediction.Clone();
            Vp8InverseTransform.AddInverseDct2(coefficients, inPlace, inPlace, offset, S);
            CopyOutsidePair(prediction, expected, offset);
            Assert.Equal(expected, inPlace);
        }
    }

    /// <summary>Everything but the two side-by-side blocks at <paramref name="offset" />, which are left as they are.</summary>
    private static void CopyOutsidePair(byte[] from, byte[] to, int offset)
    {
        for (var i = 0; i < from.Length; i++)
        {
            var inside = i >= offset && (i - offset) / S < 4 && (i - offset) % S < 8;
            if (!inside)
                to[i] = from[i];
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
}
