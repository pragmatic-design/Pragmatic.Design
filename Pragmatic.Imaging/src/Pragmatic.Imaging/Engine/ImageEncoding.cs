using System.IO.Compression;
using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.Jpeg;
using Pragmatic.Imaging.Codecs.Png;
using Pragmatic.Imaging.Codecs.WebP;

namespace Pragmatic.Imaging.Engine;

/// <summary>
///     Writes an image with the engine that handles the format: PNG, JPEG and WebP with the managed core,
///     straight into the output; AVIF, GIF, BMP and TIFF with the native library.
/// </summary>
internal static class ImageEncoding
{
    /// <summary>The deflate level of a PNG this module writes.</summary>
    /// <remarks>
    /// Fastest, because it is both smaller and faster than what the native library wrote: on the
    /// benchmarks' 2 MP PNG round trip, 2.88 MB against 3.43 MB, in about a third of the time (the
    /// benchmark's <c>Png_Managed_Fastest</c> row against <c>Png_Native</c>). Optimal would halve the
    /// file again (1.61 MB) and take twice the native library's time.
    /// </remarks>
    internal const CompressionLevel PngCompression = CompressionLevel.Fastest;

    /// <summary>Writes <paramref name="image" /> to <paramref name="output" />. <paramref name="quality" /> is JPEG's, 1 to 100.</summary>
    public static void Encode(PixelBuffer image, ImageFormat format, byte quality, Stream output)
    {
        switch (format)
        {
            case ImageFormat.Png:
                PngEncoder.Write(image, output, PngCompression);
                return;
            case ImageFormat.Jpeg:
                JpegEncoder.Write(image, output, quality);
                return;
            case ImageFormat.WebP:
                // Lossless whatever the quality, as it always was; lossy is an explicit opt-in (#853).
                WebPEncoder.EncodeLossless(image, output);
                return;
            case ImageFormat.Unknown:
                throw new ImagingException("Cannot encode to an unknown format.", ImagingError.UnsupportedFormat);
            default:
                using (var native = NativeCodec.Encode(image, format, quality))
                    output.Write(native.AsSpan());
                return;
        }
    }

    public static byte[] Encode(PixelBuffer image, ImageFormat format, byte quality)
    {
        // The WebP encoder knows its size once it has written: one array, without a stream growing.
        if (format == ImageFormat.WebP)
            return WebPEncoder.EncodeLossless(image);

        using var output = new MemoryStream();
        Encode(image, format, quality, output);
        return output.ToArray();
    }
}
