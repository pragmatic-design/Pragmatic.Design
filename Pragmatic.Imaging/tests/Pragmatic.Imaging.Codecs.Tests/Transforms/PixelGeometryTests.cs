using System.Runtime.InteropServices;
using Pragmatic.Imaging.Codecs.Transforms;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests.Transforms;

/// <summary>
///     The geometric operations move each pixel where their definition says, checked on a 3×2 image
///     whose pixels are their own indices, so a wrong mapping names the pixel that moved wrong.
/// </summary>
public class PixelGeometryTests
{
    private const int W = 3;
    private const int H = 2;

    [Fact]
    public void Rotate90_turns_clockwise()
    {
        using var source = Indexed();
        using var result = PixelGeometry.Rotate90(source);

        // Source rows 0 1 2 / 3 4 5; clockwise the left column (0, 3) becomes the top row, bottom first.
        Assert.Equal((H, W), (result.Width, result.Height));
        Assert.Equal([3u, 0, 4, 1, 5, 2], Words(result));
    }

    [Fact]
    public void Rotate180_reverses_the_pixels()
    {
        using var source = Indexed();
        using var result = PixelGeometry.Rotate180(source);

        Assert.Equal([5u, 4, 3, 2, 1, 0], Words(result));
    }

    [Fact]
    public void Rotate270_turns_counter_clockwise()
    {
        using var source = Indexed();
        using var result = PixelGeometry.Rotate270(source);

        Assert.Equal((H, W), (result.Width, result.Height));
        Assert.Equal([2u, 5, 1, 4, 0, 3], Words(result));
    }

    [Fact]
    public void Transpose_mirrors_across_the_main_diagonal()
    {
        using var source = Indexed();
        using var result = PixelGeometry.Transpose(source);

        Assert.Equal([0u, 3, 1, 4, 2, 5], Words(result));
    }

    [Fact]
    public void Transverse_mirrors_across_the_anti_diagonal()
    {
        using var source = Indexed();
        using var result = PixelGeometry.Transverse(source);

        Assert.Equal([5u, 2, 4, 1, 3, 0], Words(result));
    }

    [Fact]
    public void The_flips_mirror_in_place()
    {
        using var horizontal = Indexed();
        PixelGeometry.FlipHorizontal(horizontal);
        Assert.Equal([2u, 1, 0, 5, 4, 3], Words(horizontal));

        using var vertical = Indexed();
        PixelGeometry.FlipVertical(vertical);
        Assert.Equal([3u, 4, 5, 0, 1, 2], Words(vertical));
    }

    [Fact]
    public void Crop_copies_the_region()
    {
        using var source = Indexed();
        using var result = PixelGeometry.Crop(source, 1, 0, 2, 2);

        Assert.Equal([1u, 2, 4, 5], Words(result));
    }

    [Theory]
    [InlineData(2, 0, 2, 1)]
    [InlineData(0, 1, 1, 2)]
    [InlineData(-1, 0, 1, 1)]
    public void Crop_outside_the_image_is_refused(int x, int y, int width, int height)
    {
        using var source = Indexed();

        Assert.ThrowsAny<ArgumentException>(() => PixelGeometry.Crop(source, x, y, width, height));
    }

    private static PixelBuffer Indexed()
    {
        var image = new PixelBuffer(W, H);
        var words = MemoryMarshal.Cast<byte, uint>(image.Pixels);
        for (var i = 0; i < words.Length; i++)
            words[i] = (uint)i;
        return image;
    }

    private static uint[] Words(PixelBuffer image) => MemoryMarshal.Cast<byte, uint>(image.Pixels).ToArray();
}
