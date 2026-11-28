using Pragmatic.Imaging.Codecs.Quantization;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Codecs.Tests.Quantization;

public sealed class PaletteSearchTests
{
    [Fact]
    public void Nearest_OfRandomColours_IsWhatTryingEveryEntryFinds()
    {
        var random = new Random(855);
        Span<byte> order = stackalloc byte[256];
        for (var trial = 0; trial < 200; trial++)
        {
            var entries = random.Next(1, 257);
            var palette = new byte[entries * 3];
            random.NextBytes(palette);

            // Repeated entries and repeated greens, so ties and equal keys are reached.
            if (entries > 4)
            {
                palette.AsSpan(0, 3).CopyTo(palette.AsSpan(3));
                palette[7] = palette[1];
            }

            var skip = trial % 3 == 0 ? random.Next(entries) : -1;
            if (skip >= 0 && entries == 1)
                skip = -1;
            var search = new PaletteSearch(palette, entries, skip, order);
            for (var probe = 0; probe < 200; probe++)
            {
                int r = random.Next(256), g = random.Next(256), b = random.Next(256);
                search.Nearest(r, g, b).Should().Be(Linear(palette, entries, skip, r, g, b), $"trial {trial}, colour {r}, {g}, {b}");
            }
        }
    }

    [Fact]
    public void Nearest_OfTwoEntriesAsFarByGreenAlone_IsTheLowerIndex()
    {
        // Entry 0 is ten greens below the colour, entry 1 ten above: the search meets entry 1 first, and must not
        // stop before entry 0, whose green difference alone costs exactly the best distance found.
        byte[] palette = [100, 90, 100, 100, 110, 100];
        Span<byte> order = stackalloc byte[256];
        var search = new PaletteSearch(palette, 2, -1, order);

        search.Nearest(100, 100, 100).Should().Be(0);
    }

    private static int Linear(byte[] palette, int entries, int skip, int r, int g, int b)
    {
        var best = -1;
        var bestDistance = int.MaxValue;
        for (var k = 0; k < entries; k++)
        {
            if (k == skip)
                continue;
            var distance = PaletteSearch.Distance(palette[k * 3] - r, palette[k * 3 + 1] - g, palette[k * 3 + 2] - b);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = k;
            }
        }

        return best;
    }
}
