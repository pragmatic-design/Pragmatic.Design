namespace Pragmatic.Imaging;

/// <summary>
/// Static one-liner helpers for common image operations.
/// For advanced pipelines, use <see cref="ImagePipeline"/> directly.
/// Note: async methods offload CPU-bound native work via Task.Run. Cancellation is checked
/// before and after each native call, but cannot interrupt a native call in progress.
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
    /// Strip EXIF metadata from an image by re-encoding in the same format.
    /// Note: for JPEG this involves a decode/re-encode cycle (lossy). Use high quality (95-100) to minimize quality loss.
    /// </summary>
    public static byte[] StripExif(
        ReadOnlyMemory<byte> image, byte quality = 95, ImagingOptions? options = null)
    {
        var info = ImageInfo.FromBytes(image.Span);
        var format = info.Format is ImageFormat.Unknown ? ImageFormat.Png : info.Format;

        using var pipeline = ImagePipeline.Load(image.Span, options);
        return pipeline.Encode(format, quality);
    }

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

    /// <summary>
    /// Strip EXIF metadata from an image by re-encoding in the same format.
    /// Note: for JPEG this involves a decode/re-encode cycle (lossy). Use high quality (95-100) to minimize quality loss.
    /// </summary>
    public static Task<byte[]> StripExifAsync(
        ReadOnlyMemory<byte> image, byte quality = 95, ImagingOptions? options = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var info = ImageInfo.FromBytes(image.Span);
            var format = info.Format is ImageFormat.Unknown ? ImageFormat.Png : info.Format;

            ct.ThrowIfCancellationRequested();

            using var pipeline = ImagePipeline.Load(image.Span, options);
            return pipeline.Encode(format, quality);
        }, ct);
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
