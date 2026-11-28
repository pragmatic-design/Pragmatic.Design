using Pragmatic.Imaging.Codecs.Quantization;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Codecs.Tests.Quantization;

public sealed class PaletteRefinerTests
{
    [Fact]
    public void Refine_OfAPoorPalette_LowersTheErrorItLeaves()
    {
        var random = new Random(855);
        var rgba = new byte[5000 * 4];
        random.NextBytes(rgba);
        for (var i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;

        // Eight entries crowded in one corner of the cube.
        var palette = new byte[8 * 3];
        for (var k = 0; k < 8; k++)
            palette[k * 3] = palette[k * 3 + 1] = palette[k * 3 + 2] = (byte)(k * 4);
        var before = Error(rgba, palette);

        PaletteRefiner.Refine(rgba, palette, 8, iterations: 8);

        Error(rgba, palette).Should().BeLessThan(before / 4);
    }

    [Fact]
    public void Refine_OfAPaletteAtItsMeans_LeavesItWhereItIs()
    {
        // Two colours, each its own entry already: nothing moves.
        byte[] rgba = [10, 20, 30, 255, 200, 100, 50, 255, 10, 20, 30, 255];
        byte[] palette = [10, 20, 30, 200, 100, 50];

        PaletteRefiner.Refine(rgba, palette, 2, iterations: 5);

        palette.Should().Equal(10, 20, 30, 200, 100, 50);
    }

    private static long Error(byte[] rgba, byte[] palette)
    {
        long total = 0;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var best = int.MaxValue;
            for (var k = 0; k < palette.Length / 3; k++)
                best = Math.Min(best, PaletteSearch.Distance(rgba[i] - palette[k * 3], rgba[i + 1] - palette[k * 3 + 1], rgba[i + 2] - palette[k * 3 + 2]));
            total += best;
        }

        return total;
    }
}
