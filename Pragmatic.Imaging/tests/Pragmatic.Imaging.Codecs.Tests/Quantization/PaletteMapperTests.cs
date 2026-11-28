using Pragmatic.Imaging.Codecs.Quantization;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Codecs.Tests.Quantization;

public sealed class PaletteMapperTests
{
    private static readonly byte[] BlackWhiteAndClear = [0, 0, 0, 255, 255, 255, 7, 7, 7];

    [Fact]
    public void Map_WithoutDithering_TakesTheNearestEntryAndTheTransparentIndex()
    {
        byte[] rgba = [10, 10, 10, 255, 250, 240, 230, 255, 7, 7, 7, 0, 140, 130, 120, 255];
        var indices = new byte[4];

        PaletteMapper.Map(rgba, 4, BlackWhiteAndClear, 3, transparent: 2, dither: false, indices);

        indices.Should().Equal(0, 1, 2, 1);
    }

    [Fact]
    public void Map_WithDithering_MixesTheEntriesInTheColoursProportion()
    {
        // A flat grey a quarter of the way from black to white: undithered it is all black, dithered about a
        // quarter of its pixels are white.
        const int width = 64, height = 64;
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 64;
            rgba[i * 4 + 3] = 255;
        }

        var plain = new byte[width * height];
        var dithered = new byte[width * height];
        PaletteMapper.Map(rgba, width, BlackWhiteAndClear, 3, transparent: 2, dither: false, plain);
        PaletteMapper.Map(rgba, width, BlackWhiteAndClear, 3, transparent: 2, dither: true, dithered);

        plain.Should().OnlyContain(index => index == 0);
        var white = dithered.Count(index => index == 1) / (double)dithered.Length;
        white.Should().BeInRange(0.22, 0.28);
        dithered.Should().NotContain(2);
    }

    [Fact]
    public void Map_WithDithering_PushesNoErrorFromATransparentPixel()
    {
        // A transparent pixel between two greys that round to black: were its colour diffused, the next would turn white.
        byte[] rgba = [100, 100, 100, 255, 255, 255, 255, 0, 20, 20, 20, 255];
        var indices = new byte[3];

        PaletteMapper.Map(rgba, 3, BlackWhiteAndClear, 3, transparent: 2, dither: true, indices);

        indices.Should().Equal(0, 2, 0);
    }
}
