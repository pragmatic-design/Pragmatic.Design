using System.Runtime.InteropServices;
using Pragmatic.Imaging.Codecs.Tests.Workload;
using SkiaSharp;

namespace Pragmatic.Imaging.Benchmarks.Workload;

/// <summary>
///     The encoded inputs every engine decodes: a 4 MP JPEG (an upload from a phone) and a 2 MP PNG (a
///     screenshot or a rendered chart). Built once per process from <see cref="SyntheticPhoto" />.
/// </summary>
/// <remarks>
///     The fixtures are encoded with SkiaSharp, which neither engine under measure is: the decode of
///     each is not a measure of its own encoder's choices.
/// </remarks>
public static class Fixtures
{
    public const int JpegWidth = 2448;
    public const int JpegHeight = 1632;
    public const int PngWidth = 1600;
    public const int PngHeight = 1200;

    /// <summary>The thumbnail box every pipeline targets; at 3:2 the result is 400 × 267.</summary>
    public const int ThumbnailWidth = 400;
    public const int ThumbnailHeight = 300;

    public const int JpegQuality = 85;

    /// <summary>The thumbnail-sized photo: the encoder's input on its own.</summary>
    public const int SmallWidth = 400;
    public const int SmallHeight = 267;

    public static readonly byte[] Jpeg4Mp = Encode(JpegWidth, JpegHeight, SKEncodedImageFormat.Jpeg, 90);
    public static readonly byte[] Png2Mp = Encode(PngWidth, PngHeight, SKEncodedImageFormat.Png, 100);

    /// <summary>The 4 MP photo as a lossy WebP at quality 80, written by libwebp through SkiaSharp.</summary>
    public static readonly byte[] WebPLossy4Mp = EncodeWebP(JpegWidth, JpegHeight, SKWebpEncoderCompression.Lossy, 80);

    /// <summary>The 2 MP image as a lossless WebP, written by libwebp through SkiaSharp.</summary>
    public static readonly byte[] WebPLossless2Mp = EncodeWebP(PngWidth, PngHeight, SKWebpEncoderCompression.Lossless, 75);

    /// <summary>The 4 MP photo as raw RGBA, for the stages measured without a decode in front.</summary>
    public static readonly byte[] Rgba4Mp = SyntheticPhoto.Rgba(JpegWidth, JpegHeight);

    public static readonly byte[] RgbaSmall = SyntheticPhoto.Rgba(SmallWidth, SmallHeight);

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format, int quality)
    {
        var rgba = SyntheticPhoto.Rgba(width, height);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var data = bitmap.Encode(format, quality);
        return data.ToArray();
    }

    private static byte[] EncodeWebP(int width, int height, SKWebpEncoderCompression compression, float quality)
    {
        var rgba = SyntheticPhoto.Rgba(width, height);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var pixmap = bitmap.PeekPixels();
        using var data = pixmap.Encode(new SKWebpEncoderOptions(compression, quality))
            ?? throw new InvalidOperationException("SkiaSharp did not encode the WebP fixture.");
        return data.ToArray();
    }
}
