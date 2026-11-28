using Pragmatic.Imaging.Codecs.Quantization;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Codecs.Tests.Quantization;

public sealed class WuQuantizerTests
{
    [Fact]
    public void Quantize_OfFewerColoursThanEntries_GivesEachColourItsOwnEntry()
    {
        // Eight colours, each alone in its 5-bit cell, so each box mean is the colour itself.
        byte[][] colours = [[0, 0, 0], [255, 255, 255], [255, 0, 0], [0, 255, 0], [0, 0, 255], [128, 64, 32], [16, 200, 120], [240, 16, 240]];
        var rgba = new byte[64 * 4];
        for (var i = 0; i < 64; i++)
        {
            colours[i % 8].CopyTo(rgba, i * 4);
            rgba[i * 4 + 3] = 255;
        }

        var palette = new byte[256 * 3];
        var entries = WuQuantizer.Quantize(rgba, 256, palette);

        entries.Should().Be(8);
        var found = Enumerable.Range(0, entries).Select(k => (palette[k * 3], palette[k * 3 + 1], palette[k * 3 + 2])).ToHashSet();
        foreach (var colour in colours)
            found.Should().Contain((colour[0], colour[1], colour[2]));
    }

    [Fact]
    public void Quantize_IgnoresTransparentPixels()
    {
        var rgba = new byte[] { 10, 20, 30, 255, 200, 100, 50, 0 };
        var palette = new byte[3 * 4];

        var entries = WuQuantizer.Quantize(rgba, 4, palette);

        entries.Should().Be(1);
        palette.AsSpan(0, 3).ToArray().Should().Equal(10, 20, 30);
    }

    [Fact]
    public void Quantize_OfAGradient_KeepsTheErrorWithinTheBoxes()
    {
        // 4,096 colours into 16 entries: every pixel is within one box's spread of its entry.
        var rgba = new byte[4096 * 4];
        for (var i = 0; i < 4096; i++)
        {
            rgba[i * 4] = (byte)((i & 15) * 17);
            rgba[i * 4 + 1] = (byte)(((i >> 4) & 15) * 17);
            rgba[i * 4 + 2] = (byte)((i >> 8) * 17);
            rgba[i * 4 + 3] = 255;
        }

        var palette = new byte[16 * 3];
        var entries = WuQuantizer.Quantize(rgba, 16, palette);

        entries.Should().Be(16);
        var worst = 0;
        for (var i = 0; i < 4096; i++)
        {
            var best = int.MaxValue;
            for (var k = 0; k < entries; k++)
            {
                int dr = rgba[i * 4] - palette[k * 3], dg = rgba[i * 4 + 1] - palette[k * 3 + 1], db = rgba[i * 4 + 2] - palette[k * 3 + 2];
                best = Math.Min(best, Math.Max(Math.Abs(dr), Math.Max(Math.Abs(dg), Math.Abs(db))));
            }

            worst = Math.Max(worst, best);
        }

        // Sixteen boxes over a 16 x 16 x 16 grid split each axis into about 2.5 parts: no channel is off by more
        // than half of the widest part.
        worst.Should().BeLessThanOrEqualTo(64);
    }
}
