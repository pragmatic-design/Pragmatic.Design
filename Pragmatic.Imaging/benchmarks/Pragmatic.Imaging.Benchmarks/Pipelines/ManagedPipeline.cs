using Pragmatic.Imaging.Benchmarks.Workload;
using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.Jpeg;
using Pragmatic.Imaging.Codecs.Png;
using Pragmatic.Imaging.Codecs.Transforms;

namespace Pragmatic.Imaging.Benchmarks.Pipelines;

/// <summary>The managed codecs, stage by stage.</summary>
public static class ManagedPipeline
{
    public static int Decode(byte[] encoded)
    {
        using var image = JpegDecoder.Decode(encoded);
        return image.Width;
    }

    /// <summary>
    ///     Decode at the largest reduction that still covers the thumbnail box (the DCT does the first
    ///     part of the shrink), then the kernel for the rest. The size the thumbnail would have from the
    ///     full image is what is produced: the reduced decode changes the path, not the result's geometry.
    /// </summary>
    public static PixelBuffer Thumbnail(byte[] encoded)
    {
        var (sourceWidth, sourceHeight) = JpegDecoder.Identify(encoded);
        var (width, height) = FitBox.Fit(sourceWidth, sourceHeight, Fixtures.ThumbnailWidth, Fixtures.ThumbnailHeight);
        var scale = JpegDecoder.ScaleToCover(sourceWidth, sourceHeight, width, height);
        var image = JpegDecoder.Decode(encoded, scale);
        if (image.Width == width && image.Height == height)
            return image;
        using (image)
            return Resampler.Resize(image, width, height, Codecs.Transforms.ResizeFilter.Lanczos3);
    }

    public static int DecodeThumbnail(byte[] encoded)
    {
        using var thumbnail = Thumbnail(encoded);
        return thumbnail.Width;
    }

    public static byte[] DecodeThumbnailEncodeJpeg(byte[] encoded)
    {
        using var thumbnail = Thumbnail(encoded);
        using var output = new MemoryStream();
        JpegEncoder.Write(thumbnail, output, Fixtures.JpegQuality);
        return output.ToArray();
    }

    /// <summary>The encoder alone, from pixels already in a buffer.</summary>
    public static byte[] EncodeJpeg(PixelBuffer image, bool optimizeCoding = true)
    {
        using var output = new MemoryStream();
        JpegEncoder.Write(image, output, Fixtures.JpegQuality, optimizeCoding);
        return output.ToArray();
    }

    /// <summary>Thumbnail from pixels already in a <see cref="PixelBuffer" />: the resize alone.</summary>
    public static int ThumbnailFromRgba(PixelBuffer source)
    {
        var (width, height) = FitBox.Fit(source.Width, source.Height, Fixtures.ThumbnailWidth, Fixtures.ThumbnailHeight);
        using var thumbnail = Resampler.Resize(source, width, height, Codecs.Transforms.ResizeFilter.Lanczos3);
        return thumbnail.Width;
    }

    public static byte[] DecodeEncodePng(byte[] encoded, System.IO.Compression.CompressionLevel level = System.IO.Compression.CompressionLevel.Optimal)
    {
        using var image = PngDecoder.Decode(encoded);
        using var output = new MemoryStream();
        PngEncoder.Write(image, output, level);
        return output.ToArray();
    }
}
