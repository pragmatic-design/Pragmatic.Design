using Pragmatic.Imaging.Codecs.Tests.Workload;
using Pragmatic.Imaging.Codecs.Transforms;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests;

public class ResamplerTests
{
    [Theory]
    [InlineData(ResizeFilter.Triangle)]
    [InlineData(ResizeFilter.CatmullRom)]
    [InlineData(ResizeFilter.Lanczos3)]
    [InlineData(ResizeFilter.Nearest)]
    public void Resizing_to_the_same_size_leaves_every_pixel_as_it_was(ResizeFilter filter)
    {
        const int width = 97;
        const int height = 41;
        using var source = Load(SyntheticPhoto.Rgba(width, height), width, height);

        using var same = Resampler.Resize(source, width, height, filter);

        Assert.True(same.Pixels.SequenceEqual(source.Pixels), $"{filter} at scale 1 is not the identity");
    }

    [Theory]
    [InlineData(ResizeFilter.Triangle)]
    [InlineData(ResizeFilter.CatmullRom)]
    [InlineData(ResizeFilter.Gaussian)]
    [InlineData(ResizeFilter.Lanczos3)]
    [InlineData(ResizeFilter.Nearest)]
    public void A_solid_image_stays_solid_at_any_size(ResizeFilter filter)
    {
        using var source = new PixelBuffer(640, 480);
        var pixels = source.Pixels;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 200;
            pixels[i + 1] = 30;
            pixels[i + 2] = 90;
            pixels[i + 3] = 255;
        }

        using var small = Resampler.Resize(source, 100, 75, filter);
        using var big = Resampler.Resize(source, 1000, 700, filter);

        foreach (var image in new[] { small, big })
        {
            var p = image.Pixels;
            for (var i = 0; i < p.Length; i += 4)
            {
                Assert.Equal(200, p[i]);
                Assert.Equal(30, p[i + 1]);
                Assert.Equal(90, p[i + 2]);
                Assert.Equal(255, p[i + 3]);
            }
        }
    }

    [Fact]
    public void A_downscaled_thumbnail_is_close_to_skias_of_the_same_photo()
    {
        const int width = 2448;
        const int height = 1632;
        var rgba = SyntheticPhoto.Rgba(width, height);
        var (thumbWidth, thumbHeight) = FitBox.Fit(width, height, 400, 300);

        using var source = Load(rgba, width, height);
        using var ours = Resampler.Resize(source, thumbWidth, thumbHeight, ResizeFilter.CatmullRom);
        var theirs = SkiaOracle.ResizeMitchell(rgba, width, height, thumbWidth, thumbHeight);

        // Different cubics (Catmull-Rom against Mitchell) on hard edges: a plausibility bound, not a match.
        var psnr = Psnr(ours.Pixels, theirs);
        Assert.True(psnr > 30, $"PSNR against Skia's Mitchell downscale is {psnr:F1} dB");
    }

    [Fact]
    public void The_fast_path_is_close_to_the_exact_kernel()
    {
        const int width = 2448;
        const int height = 1632;
        var rgba = SyntheticPhoto.Rgba(width, height);
        var (thumbWidth, thumbHeight) = FitBox.Fit(width, height, 400, 300);

        using var source = Load(rgba, width, height);
        using var fast = Resampler.Resize(source, thumbWidth, thumbHeight, ResizeFilter.Lanczos3);
        using var exact = Resampler.Resize(source, thumbWidth, thumbHeight, ResizeFilter.Lanczos3, exact: true);

        var psnr = Psnr(fast.Pixels, exact.Pixels);
        Assert.True(psnr > 38, $"PSNR between the halving path and the exact kernel is {psnr:F1} dB");
    }

    [Fact]
    public void Halving_averages_each_2x2_block_with_rounding()
    {
        using var source = new PixelBuffer(6, 4);
        var p = source.Pixels;
        for (var i = 0; i < p.Length; i++)
            p[i] = (byte)(i * 37);
        // Opaque: the integer path. A translucent image is averaged premultiplied (the test below).
        for (var i = 3; i < p.Length; i += 4)
            p[i] = 255;

        using var half = Resampler.Resize(source, 3, 2, ResizeFilter.Triangle);

        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                for (var c = 0; c < 4; c++)
                {
                    var a = p[(y * 2 * 6 + x * 2) * 4 + c];
                    var b = p[(y * 2 * 6 + x * 2 + 1) * 4 + c];
                    var d = p[((y * 2 + 1) * 6 + x * 2) * 4 + c];
                    var e = p[((y * 2 + 1) * 6 + x * 2 + 1) * 4 + c];
                    Assert.Equal((a + b + d + e + 2) >> 2, half.Pixels[(y * 3 + x) * 4 + c]);
                }
            }
        }
    }

    [Fact]
    public void Halving_a_translucent_image_averages_colour_weighted_by_alpha()
    {
        using var source = new PixelBuffer(6, 4);
        for (var i = 0; i < source.ByteLength; i++)
            source.Pixels[i] = (byte)(i * 37);
        var p = source.Pixels.ToArray();

        using var half = Resampler.Resize(source, 3, 2, ResizeFilter.Triangle);

        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                int[] at = [(y * 2 * 6 + x * 2) * 4, (y * 2 * 6 + x * 2 + 1) * 4, ((y * 2 + 1) * 6 + x * 2) * 4, ((y * 2 + 1) * 6 + x * 2 + 1) * 4];
                var alphaSum = at.Sum(o => p[o + 3]);
                var o = (y * 3 + x) * 4;
                Assert.Equal((int)Math.Floor(alphaSum / 4.0 + 0.5), half.Pixels[o + 3]);
                for (var c = 0; c < 3; c++)
                {
                    var weighted = at.Sum(s => p[s + c] * p[s + 3]);
                    var expected = alphaSum == 0 ? 0 : (int)Math.Floor((double)weighted / alphaSum + 0.5);
                    Assert.Equal(expected, half.Pixels[o + c]);
                }
            }
        }
    }

    [Fact]
    public void Fit_keeps_the_aspect_ratio_and_never_upscales()
    {
        Assert.Equal((400, 267), FitBox.Fit(2448, 1632, 400, 300));
        Assert.Equal((225, 300), FitBox.Fit(1500, 2000, 400, 300));
        Assert.Equal((120, 80), FitBox.Fit(120, 80, 400, 300));
        Assert.Equal((400, 267), FitBox.Fit(120, 80, 400, 300, allowUpscale: true));
    }

    private static PixelBuffer Load(byte[] rgba, int width, int height)
    {
        var image = new PixelBuffer(width, height);
        rgba.CopyTo(image.Pixels);
        return image;
    }

    private static double Psnr(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        Assert.Equal(a.Length, b.Length);
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            double d = a[i] - b[i];
            sum += d * d;
        }

        var mse = sum / a.Length;
        return mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse);
    }
}
