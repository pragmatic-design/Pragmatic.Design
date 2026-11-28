using System.Runtime.InteropServices;
using Pragmatic.Testing.Assertions;
using SkiaSharp;

namespace Pragmatic.Imaging.Tests;

/// <summary>
///     Opaque red beside transparent black, the classic case: resized on straight alpha, the edge pixels average the
///     red with the black of pixels nobody sees, and come out dark red at half alpha, the halo around a transparent
///     thumbnail. Resampled premultiplied, every pixel that shows anything shows pure red, whatever its alpha.
/// </summary>
public class AResizedTransparentImageHasNoDarkFringeTests
{
    private const int Side = 64;

    [Theory]
    [InlineData(16, 16)] // two exact halvings, no kernel
    [InlineData(23, 23)] // a halving, then Lanczos over the rest
    [InlineData(40, 40)] // Lanczos alone
    public void The_edge_pixels_of_a_resized_transparent_png_keep_their_colour(uint width, uint height)
    {
        using var pipeline = ImagePipeline.Load(HalfRedHalfTransparent());
        pipeline.Resize(width, height);
        var rgba = DecodeWithSkia(pipeline.Encode(ImageFormat.Png));

        var edge = 0;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var alpha = rgba[i + 3];
            if (alpha == 0)
                continue;
            if (alpha < 255)
                edge++;
            rgba[i].Should().BeGreaterThanOrEqualTo((byte)254, $"pixel {i / 4} has alpha {alpha} and red {rgba[i]}: the transparent black was averaged in");
            rgba[i + 1].Should().Be((byte)0);
            rgba[i + 2].Should().Be((byte)0);
        }

        edge.Should().BeGreaterThan(0, "the resize must leave translucent edge pixels, or the test proves nothing");
    }

    private static byte[] HalfRedHalfTransparent()
    {
        var pixels = new byte[Side * Side * 4];
        for (var y = 0; y < Side; y++)
        {
            // The boundary off the halving grid, so a 2x2 block straddles it.
            for (var x = 0; x < 29; x++)
            {
                var at = (y * Side + x) * 4;
                pixels[at] = 255;
                pixels[at + 3] = 255;
            }
        }

        var info = new SKImageInfo(Side, Side, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] DecodeWithSkia(byte[] png)
    {
        using var skData = SKData.CreateCopy(png);
        using var codec = SKCodec.Create(skData) ?? throw new InvalidOperationException("Skia cannot read the bytes.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success)
            throw new InvalidOperationException($"Skia decode: {result}.");

        var rgba = new byte[info.BytesSize];
        Marshal.Copy(bitmap.GetPixels(), rgba, 0, rgba.Length);
        return rgba;
    }
}
