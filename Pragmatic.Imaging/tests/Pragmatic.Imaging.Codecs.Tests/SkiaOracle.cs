using System.Runtime.InteropServices;
using SkiaSharp;

namespace Pragmatic.Imaging.Codecs.Tests;

/// <summary>
///     SkiaSharp as the independent codec: what it encodes we must decode to the same pixels, and what
///     we encode it must decode to the pixels we started from. Unpremultiplied RGBA throughout, so an
///     alpha below 255 does not change the colour on the way.
/// </summary>
internal static class SkiaOracle
{
    public static byte[] EncodePngRgba(byte[] rgba, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static byte[] EncodeJpegRgba(byte[] rgba, int width, int height, int quality)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    public static byte[] EncodeJpegGray8(byte[] gray, int width, int height, int quality)
    {
        var info = new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(gray, 0, bitmap.GetPixels(), gray.Length);
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    public static byte[] EncodePngGray8(byte[] gray, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(gray, 0, bitmap.GetPixels(), gray.Length);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Skia's Mitchell cubic downscale, unpremultiplied RGBA in and out.</summary>
    public static byte[] ResizeMitchell(byte[] rgba, int width, int height, int targetWidth, int targetHeight)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        var targetInfo = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var resized = bitmap.Resize(targetInfo, new SKSamplingOptions(SKCubicResampler.Mitchell));
        var result = new byte[targetInfo.BytesSize];
        Marshal.Copy(resized.GetPixels(), result, 0, result.Length);
        return result;
    }

    /// <summary>Decodes to unpremultiplied 8-bit RGBA, whatever the file's own colour type.</summary>
    public static (byte[] Rgba, int Width, int Height) DecodeRgba(byte[] encoded)
    {
        using var skData = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(skData) ?? throw new InvalidOperationException("Skia cannot read the bytes.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success)
            throw new InvalidOperationException($"Skia decode: {result}.");

        var rgba = new byte[info.BytesSize];
        Marshal.Copy(bitmap.GetPixels(), rgba, 0, rgba.Length);
        return (rgba, info.Width, info.Height);
    }
}
