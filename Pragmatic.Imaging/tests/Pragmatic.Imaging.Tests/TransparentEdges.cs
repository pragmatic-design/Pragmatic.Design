using System.Runtime.InteropServices;
using Pragmatic.Testing.Assertions;
using SkiaSharp;

namespace Pragmatic.Imaging.Tests;

/// <summary>
///     Opaque red beside transparent black, the case where an operation that averages neighbours on straight alpha
///     darkens an edge: the red is mixed with the black of pixels nobody sees and comes out dark red at partial alpha.
///     Done premultiplied, every pixel that shows anything shows pure red, whatever its alpha.
/// </summary>
internal static class TransparentEdges
{
    private const int Side = 64;

    /// <summary>The image as a PNG, written by Skia: opaque red on the left 29 columns, transparent black on the rest.</summary>
    public static byte[] HalfRedHalfTransparent()
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

    /// <summary>
    ///     Opaque mid grey on the left 29 columns, transparent white on the rest: the case for a sharpen, which on
    ///     straight alpha subtracts the hidden white from the visible grey and darkens the edge below 128. Red beside
    ///     black hides that, as the red is pushed above 255 and clamped.
    /// </summary>
    public static byte[] GreyBesideTransparentWhite()
    {
        var pixels = new byte[Side * Side * 4];
        for (var y = 0; y < Side; y++)
        {
            for (var x = 0; x < Side; x++)
            {
                var at = (y * Side + x) * 4;
                var visible = x < 29;
                pixels[at] = pixels[at + 1] = pixels[at + 2] = visible ? (byte)128 : (byte)255;
                pixels[at + 3] = visible ? (byte)255 : (byte)0;
            }
        }

        var info = new SKImageInfo(Side, Side, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Every pixel of <paramref name="png" /> that shows anything is grey 128, within one unit.</summary>
    public static void AssertVisiblePixelsStayGrey(byte[] png)
    {
        var rgba = DecodeWithSkia(png);
        var visible = 0;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0)
                continue;
            visible++;
            for (var c = 0; c < 3; c++)
                Math.Abs(rgba[i + c] - 128).Should().BeLessThanOrEqualTo(1, $"pixel {i / 4} (alpha {rgba[i + 3]}) has channel {c} at {rgba[i + c]}: the hidden white leaked into it");
        }

        visible.Should().BeGreaterThan(0);
    }

    /// <summary>
    ///     Every pixel of <paramref name="png" /> that shows anything is pure red, and some of them are translucent, or
    ///     the operation left no edge and the check proves nothing.
    /// </summary>
    public static void AssertNoDarkFringe(byte[] png)
    {
        var rgba = DecodeWithSkia(png);
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

        edge.Should().BeGreaterThan(0, "the operation must leave translucent edge pixels, or the test proves nothing");
    }

    // Unpremultiplied, so a translucent pixel reads back without a rounding of Skia's own.
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
