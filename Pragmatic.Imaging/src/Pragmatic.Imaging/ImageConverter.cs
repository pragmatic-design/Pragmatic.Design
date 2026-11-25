using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.Metadata;

namespace Pragmatic.Imaging;

/// <summary>
/// Static one-liner helpers for common image operations.
/// For advanced pipelines, use <see cref="ImagePipeline"/> directly.
/// Note: async methods offload CPU-bound work via Task.Run. Cancellation is checked
/// before and after each step, but cannot interrupt a decode or encode in progress.
/// </summary>
public static class ImageConverter
{
    /// <summary>Create a thumbnail that fits within the given bounds, preserving aspect ratio.</summary>
    public static byte[] Thumbnail(
        ReadOnlyMemory<byte> image, uint maxWidth, uint maxHeight,
        ImageFormat format = ImageFormat.Png, byte quality = 90,
        ResizeFilter filter = ResizeFilter.Lanczos3,
        ImagingOptions? options = null)
    {
        using var pipeline = ImagePipeline.Load(image.Span, options);
        pipeline.Thumbnail(maxWidth, maxHeight, filter);
        return pipeline.Encode(format, quality);
    }

    /// <summary>Convert an image to the specified format.</summary>
    public static byte[] Convert(
        ReadOnlyMemory<byte> image, ImageFormat format,
        byte quality = 90, ImagingOptions? options = null)
    {
        using var pipeline = ImagePipeline.Load(image.Span, options);
        return pipeline.Encode(format, quality);
    }

    /// <summary>
    /// Strip EXIF and XMP metadata from an image.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A JPEG or PNG is not decoded: the file is copied without its metadata segments (JPEG APP1, PNG
    /// eXIf), so a JPEG keeps its exact compressed data and loses no quality. The EXIF orientation is
    /// kept, alone in a new EXIF block, so a photo still shows upright; camera, GPS and timestamps go.
    /// Colour profiles, comments, IPTC (APP13) and PNG text chunks stay.
    /// </para>
    /// <para>
    /// Any other format is decoded and re-encoded in the same format, which drops what its encoder does
    /// not write; <paramref name="quality"/> applies only there.
    /// </para>
    /// </remarks>
    public static byte[] StripExif(
        ReadOnlyMemory<byte> image, byte quality = 95, ImagingOptions? options = null)
        => StripMetadata(image.Span, quality, options);

    /// <summary>Resize an image to exact dimensions.</summary>
    public static byte[] Resize(
        ReadOnlyMemory<byte> image, uint width, uint height,
        ImageFormat format = ImageFormat.Png, byte quality = 90,
        ResizeFilter filter = ResizeFilter.Lanczos3,
        ImagingOptions? options = null)
    {
        using var pipeline = ImagePipeline.Load(image.Span, options);
        pipeline.Resize(width, height, filter);
        return pipeline.Encode(format, quality);
    }

    /// <summary>Create a thumbnail that fits within the given bounds, preserving aspect ratio.</summary>
    public static Task<byte[]> ThumbnailAsync(
        ReadOnlyMemory<byte> image, uint maxWidth, uint maxHeight,
        ImageFormat format = ImageFormat.Png, byte quality = 90,
        ResizeFilter filter = ResizeFilter.Lanczos3,
        ImagingOptions? options = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var pipeline = ImagePipeline.Load(image.Span, options);
            pipeline.Thumbnail(maxWidth, maxHeight, filter);
            ct.ThrowIfCancellationRequested();
            return pipeline.Encode(format, quality);
        }, ct);
    }

    /// <summary>Convert an image to the specified format.</summary>
    public static Task<byte[]> ConvertAsync(
        ReadOnlyMemory<byte> image, ImageFormat format,
        byte quality = 90, ImagingOptions? options = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var pipeline = ImagePipeline.Load(image.Span, options);
            return pipeline.Encode(format, quality);
        }, ct);
    }

    /// <summary>Strip EXIF and XMP metadata from an image, as <see cref="StripExif"/> does.</summary>
    public static Task<byte[]> StripExifAsync(
        ReadOnlyMemory<byte> image, byte quality = 95, ImagingOptions? options = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return StripMetadata(image.Span, quality, options);
        }, ct);
    }

    private static byte[] StripMetadata(ReadOnlySpan<byte> data, byte quality, ImagingOptions? options)
    {
        options ??= ImagingOptions.Default;
        options.ValidateInput(data.Length);
        var info = ImageInfo.FromBytes(data);
        options.ValidateDecoded(info.Width, info.Height);
        options.ValidateFormat(info.Format);

        try
        {
            switch (info.Format)
            {
                case ImageFormat.Jpeg:
                    return MetadataStripper.StripJpeg(data);
                case ImageFormat.Png:
                    return MetadataStripper.StripPng(data);
            }
        }
        catch (ImageDecodeException e)
        {
            throw new ImagingException(e.Message, e, ImagingError.DecodeFailed);
        }

        using var pipeline = ImagePipeline.Load(data, options);
        return pipeline.Encode(info.Format is ImageFormat.Unknown ? ImageFormat.Png : info.Format, quality);
    }

    /// <summary>Resize an image to exact dimensions.</summary>
    public static Task<byte[]> ResizeAsync(
        ReadOnlyMemory<byte> image, uint width, uint height,
        ImageFormat format = ImageFormat.Png, byte quality = 90,
        ResizeFilter filter = ResizeFilter.Lanczos3,
        ImagingOptions? options = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var pipeline = ImagePipeline.Load(image.Span, options);
            pipeline.Resize(width, height, filter);
            ct.ThrowIfCancellationRequested();
            return pipeline.Encode(format, quality);
        }, ct);
    }
}
