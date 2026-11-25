using Pragmatic.Imaging.Codecs.Jpeg;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests;

/// <summary>
///     The AAN transform against the plain matrix product, quantised with the same table: the two
///     round the same way except where a coefficient lands within float noise of a half, so one step
///     apart is allowed and anything more is a wrong factor or a wrong lane.
/// </summary>
public class JpegForwardDctTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 85)]
    [InlineData(99, 85)]
    [InlineData(123, 50)]
    public void Aan_matches_the_matrix_product_within_one_step(int seed, int quality)
    {
        if (!JpegForwardDctAvx2.IsSupported)
            return; // Nothing to compare on this machine; the scalar path is what the matrix product is.
        var random = new Random(seed);
        var (quant, _) = JpegQuantization.ForQuality(quality);
        var reciprocals = new float[64];
        JpegForwardDctAvx2.BuildReciprocals(quant, reciprocals);

        var samples = new byte[64];
        var expected = new float[64];
        var actual = new short[64];
        var exact = 0;
        const int blocks = 200;
        for (var b = 0; b < blocks; b++)
        {
            // Smooth-ish blocks with noise, as photographs have, and a few flat or saturated ones.
            var baseValue = random.Next(0, 256);
            var slopeX = random.Next(-12, 13);
            var slopeY = random.Next(-12, 13);
            var noise = b % 10 == 0 ? 0 : random.Next(0, 40);
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                    samples[y * 8 + x] = (byte)Math.Clamp(baseValue + slopeX * x + slopeY * y + random.Next(-noise, noise + 1), 0, 255);
            }

            JpegForwardDct.Transform(samples, 8, expected);
            JpegForwardDctAvx2.TransformAndQuantize(samples, 8, reciprocals, actual);

            for (var v = 0; v < 8; v++)
            {
                for (var u = 0; u < 8; u++)
                {
                    var want = (int)MathF.Round(expected[v * 8 + u] / quant[v * 8 + u], MidpointRounding.ToEven);
                    int got = actual[u * 8 + v];
                    Assert.True(Math.Abs(want - got) <= 1, $"block {b}, coefficient ({v}, {u}): AAN {got}, matrix {want}");
                    if (want == got)
                        exact++;
                }
            }
        }

        // Almost all agree exactly; the one-step cases are the half-way roundings.
        Assert.True(exact > blocks * 64 * 0.98, $"only {exact} of {blocks * 64} coefficients agree exactly");
    }
}
