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

    /// <summary>The 2 MP image as raw RGBA: the input of the WebP encoders.</summary>
    public static readonly byte[] Rgba2Mp = SyntheticPhoto.Rgba(PngWidth, PngHeight);

    /// <summary>
    ///     The 2 MP image as a 24-bit BMP, the most common kind, and as the 32-bit one with alpha that both encoders
    ///     write. SkiaSharp writes no BMP: these come from <see cref="Bmp" /> here, which neither engine is.
    /// </summary>
    public static readonly byte[] Bmp24Of2Mp = Bmp(Rgba2Mp, PngWidth, PngHeight, alpha: false);

    public static readonly byte[] Bmp32Of2Mp = Bmp(Rgba2Mp, PngWidth, PngHeight, alpha: true);

    /// <summary>The size of <see cref="RgbaPhoto" />.</summary>
    public const int PhotoWidth = 600;
    public const int PhotoHeight = 400;

    /// <summary>
    ///     A photograph as raw RGBA: scikit-image's coffee (CC0, the visual tests' reference, provenance in its
    ///     references.json), decoded by SkiaSharp. A camera's textures and noise make coefficient levels the
    ///     synthetic photo does not, and a lossy encoder's mode choice spends its time on them.
    /// </summary>
    public static readonly byte[] RgbaPhoto = DecodePhoto(Path.Combine(AppContext.BaseDirectory, "References", "Photographs", "coffee.png"));

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format, int quality)
    {
        var rgba = SyntheticPhoto.Rgba(width, height);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var data = bitmap.Encode(format, quality);
        return data.ToArray();
    }

    private static byte[] DecodePhoto(string path)
    {
        using var decoded = SKBitmap.Decode(path) ?? throw new InvalidOperationException($"SkiaSharp did not decode {path}.");
        if (decoded.Width != PhotoWidth || decoded.Height != PhotoHeight)
            throw new InvalidOperationException($"{path} is {decoded.Width} x {decoded.Height}, not {PhotoWidth} x {PhotoHeight}.");

        var info = new SKImageInfo(PhotoWidth, PhotoHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var rgba = new SKBitmap(info);
        if (!decoded.CopyTo(rgba, SKColorType.Rgba8888))
            throw new InvalidOperationException($"SkiaSharp did not convert {path} to RGBA.");
        var pixels = new byte[info.BytesSize];
        Marshal.Copy(rgba.GetPixels(), pixels, 0, pixels.Length);
        return pixels;
    }

    // A bottom-up BMP: 24-bit BGR with an info header, or 32-bit BGRA as bitfields with a V4 header.
    private static byte[] Bmp(byte[] rgba, int width, int height, bool alpha)
    {
        var header = alpha ? 108 : 40;
        var stride = alpha ? width * 4 : (width * 3 + 3) / 4 * 4;
        var offset = 14 + header;
        var file = new byte[offset + stride * height];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BitConverter.GetBytes(file.Length).CopyTo(file, 2);
        BitConverter.GetBytes(offset).CopyTo(file, 10);
        BitConverter.GetBytes(header).CopyTo(file, 14);
        BitConverter.GetBytes(width).CopyTo(file, 18);
        BitConverter.GetBytes(height).CopyTo(file, 22);
        BitConverter.GetBytes((short)1).CopyTo(file, 26);
        BitConverter.GetBytes((short)(alpha ? 32 : 24)).CopyTo(file, 28);
        if (alpha)
        {
            BitConverter.GetBytes(3).CopyTo(file, 30);
            BitConverter.GetBytes(0x00FF0000).CopyTo(file, 54);
            BitConverter.GetBytes(0x0000FF00).CopyTo(file, 58);
            BitConverter.GetBytes(0x000000FF).CopyTo(file, 62);
            BitConverter.GetBytes(unchecked((int)0xFF000000)).CopyTo(file, 66);
        }

        var channels = alpha ? 4 : 3;
        for (var y = 0; y < height; y++)
        {
            var row = offset + (height - 1 - y) * stride;
            for (var x = 0; x < width; x++)
            {
                var source = (y * width + x) * 4;
                var target = row + x * channels;
                file[target] = rgba[source + 2];
                file[target + 1] = rgba[source + 1];
                file[target + 2] = rgba[source];
                if (alpha)
                    file[target + 3] = rgba[source + 3];
            }
        }

        return file;
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
