using Pragmatic.Imaging.Codecs;
using SkiaSharp;

namespace Pragmatic.Imaging.Benchmarks.Pipelines;

/// <summary>
///     libwebp's lossy encoder, through SkiaSharp, on pixels already in memory: the reference the managed
///     lossy encoder is measured against, since the native library it replaced never wrote lossy WebP.
/// </summary>
internal static unsafe class LibWebP
{
    public static byte[] EncodeLossy(PixelBuffer image, int quality)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var pixmap = new SKPixmap(info, (nint)image.Pointer, info.RowBytes);
        using var data = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, quality))
                         ?? throw new InvalidOperationException("Skia cannot encode WebP.");
        return data.ToArray();
    }
}
