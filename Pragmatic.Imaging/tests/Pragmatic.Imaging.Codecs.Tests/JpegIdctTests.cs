using Pragmatic.Imaging.Codecs.Jpeg;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests;

/// <summary>
///     Both inverse transforms against the samples a forward transform started from: blocks a file can
///     actually hold, not random coefficients (those can carry more energy than any image and overflow
///     the integer path, which a real block never does).
/// </summary>
public class JpegIdctTests
{
    [Fact]
    public void Both_transforms_recover_the_samples_a_forward_transform_started_from()
    {
        var random = new Random(12345);
        var samples = new byte[64];
        var coefficients = new float[64];
        var block = new short[64];
        var scalar = new byte[64];
        var vector = new byte[64];
        var worstScalar = 0;
        var worstVector = 0;
        var worstBetween = 0;

        for (var trial = 0; trial < 2000; trial++)
        {
            // A gradient with an edge and some grain: what a block of a photo looks like.
            var baseValue = random.Next(0, 256);
            var slopeX = random.Next(-30, 31);
            var slopeY = random.Next(-30, 31);
            var edgeAt = random.Next(0, 9);
            var edgeStep = random.Next(-120, 121);
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                    samples[y * 8 + x] = (byte)Math.Clamp(baseValue + slopeX * x + slopeY * y + (x >= edgeAt ? edgeStep : 0) + random.Next(-6, 7), 0, 255);
            }

            JpegForwardDct.Transform(samples, 8, coefficients);
            for (var i = 0; i < 64; i++)
                block[i] = (short)MathF.Round(coefficients[i]);

            // Rounding the coefficients to integers moves the samples: the reference is the exact inverse
            // of the rounded coefficients, in double precision, not the samples themselves.
            var reference = ReferenceInverse(block);

            JpegIdct.TransformScalar(block, scalar, 8);
            if (JpegIdctAvx2.IsSupported)
                JpegIdctAvx2.Transform(block, vector, 8);
            else
                scalar.CopyTo(vector, 0);

            for (var i = 0; i < 64; i++)
            {
                worstScalar = Math.Max(worstScalar, Math.Abs(scalar[i] - reference[i]));
                worstVector = Math.Max(worstVector, Math.Abs(vector[i] - reference[i]));
                worstBetween = Math.Max(worstBetween, Math.Abs(scalar[i] - vector[i]));
            }
        }

        Assert.True(worstScalar <= 1, $"scalar IDCT is off by up to {worstScalar} levels");
        Assert.True(worstVector <= 1, $"AVX2 IDCT is off by up to {worstVector} levels");
        Assert.True(worstBetween <= 1, $"the two transforms differ by up to {worstBetween} levels");
    }

    private static byte[] ReferenceInverse(short[] block)
    {
        var output = new byte[64];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var sum = 0.0;
                for (var u = 0; u < 8; u++)
                {
                    var cu = u == 0 ? Math.Sqrt(0.5) : 1.0;
                    for (var v = 0; v < 8; v++)
                    {
                        var cv = v == 0 ? Math.Sqrt(0.5) : 1.0;
                        sum += cu * cv * block[u * 8 + v] * Math.Cos((2 * y + 1) * u * Math.PI / 16) * Math.Cos((2 * x + 1) * v * Math.PI / 16);
                    }
                }

                output[y * 8 + x] = (byte)Math.Clamp(Math.Round(sum / 4 + 128), 0, 255);
            }
        }

        return output;
    }

    [Fact]
    public void A_transposed_block_gives_the_samples_of_the_natural_one_within_a_level()
    {
        var random = new Random(777);
        var natural = new short[64];
        var transposed = new short[64];
        var fromNatural = new byte[64];
        var fromTransposed = new byte[64];
        var worst = 0;

        for (var trial = 0; trial < 500; trial++)
        {
            FillLikeAQuantisedBlock(random, natural);
            for (var v = 0; v < 8; v++)
            {
                for (var u = 0; u < 8; u++)
                    transposed[u * 8 + v] = natural[v * 8 + u];
            }

            JpegIdct.Transform(natural, fromNatural, 8);
            JpegIdct.TransformTransposed(transposed, fromTransposed, 8);
            for (var i = 0; i < 64; i++)
                worst = Math.Max(worst, Math.Abs(fromNatural[i] - fromTransposed[i]));
        }

        // The passes run in the other order, so the float rounding can differ by one level, no more.
        Assert.True(worst <= 1, $"the transposed transform differs from the natural one by up to {worst} levels");
    }

    /// <summary>Coefficients that fall off with frequency, as a quantised block's do.</summary>
    private static void FillLikeAQuantisedBlock(Random random, short[] block)
    {
        for (var i = 0; i < 64; i++)
        {
            var reach = 1 + 2000 / (1 + (i % 8) + (i / 8)) / 8;
            block[i] = (short)random.Next(-reach, reach + 1);
        }
    }

    [Fact]
    public void A_dc_only_block_is_flat()
    {
        var block = new short[64];
        block[0] = 80; // 80 / 8 + 128 = 138
        var output = new byte[64];

        JpegIdct.Transform(block, output, 8);

        Assert.All(output, v => Assert.Equal(138, v));
    }
}
