namespace Pragmatic.Imaging;

/// <summary>
/// Safety limits and defaults for image processing operations.
/// Prevents decompression bombs and excessive memory usage.
/// </summary>
public sealed record ImagingOptions
{
    /// <summary>Maximum input file size in bytes before decode. Default: 100 MB.</summary>
    public long MaxInputBytes { get; init; } = 100 * 1024 * 1024;

    /// <summary>Maximum decoded image size in megapixels. Default: 100 MP (~400 MB RGBA).</summary>
    public int MaxMegapixels { get; init; } = 100;

    /// <summary>Maximum decoded width in pixels. <c>0</c> (default) means unlimited — the megapixel cap still applies.</summary>
    public uint MaxWidth { get; init; }

    /// <summary>Maximum decoded height in pixels. <c>0</c> (default) means unlimited — the megapixel cap still applies.</summary>
    public uint MaxHeight { get; init; }

    /// <summary>
    /// Allow-list of accepted input formats. <c>null</c> (default) accepts any decodable format.
    /// Supply an explicit set for untrusted uploads, e.g. <c>[ImageFormat.Jpeg, ImageFormat.Png, ImageFormat.WebP]</c>.
    /// </summary>
    public IReadOnlyCollection<ImageFormat>? AllowedFormats { get; init; }

    /// <summary>Default options with safe limits.</summary>
    public static ImagingOptions Default { get; } = new();

    /// <summary>Relaxed options for server-side batch processing.</summary>
    public static ImagingOptions Relaxed { get; } = new()
    {
        MaxInputBytes = 500 * 1024 * 1024,
        MaxMegapixels = 500
    };

    /// <summary>Strict options for user-uploaded content.</summary>
    public static ImagingOptions Strict { get; } = new()
    {
        MaxInputBytes = 20 * 1024 * 1024,
        MaxMegapixels = 25
    };

    internal void ValidateInput(long inputLength)
    {
        if (inputLength > MaxInputBytes)
            throw new ImagingException(
                $"Input size ({inputLength} bytes) exceeds limit ({MaxInputBytes} bytes).",
                ImagingError.InputTooLarge);
    }

    internal void ValidateDecoded(uint width, uint height)
    {
        if (width == 0 || height == 0)
            throw new ImagingException(
                $"Decoded image has zero dimensions ({width}x{height}).",
                ImagingError.DecodeFailed);

        if (MaxWidth != 0 && width > MaxWidth)
            throw new ImagingException(
                $"Decoded width ({width}px) exceeds limit ({MaxWidth}px).",
                ImagingError.MaxWidthExceeded);

        if (MaxHeight != 0 && height > MaxHeight)
            throw new ImagingException(
                $"Decoded height ({height}px) exceeds limit ({MaxHeight}px).",
                ImagingError.MaxHeightExceeded);

        var totalPixels = (long)width * height;
        var limitPixels = (long)MaxMegapixels * 1_000_000;
        if (totalPixels > limitPixels)
            throw new ImagingException(
                $"Decoded image ({width}x{height}, {totalPixels / 1_000_000.0:F1} MP) exceeds limit ({MaxMegapixels} MP).",
                ImagingError.MaxMegapixelsExceeded);
    }

    internal void ValidateFormat(ImageFormat format)
    {
        // null = unrestricted. An explicit (even empty) set is a fail-closed allow-list.
        if (AllowedFormats is null)
            return;

        if (!AllowedFormats.Contains(format))
            throw new ImagingException(
                $"Decoded format ({format}) is not in the allowed set ({string.Join(", ", AllowedFormats)}).",
                ImagingError.FormatNotAllowed);
    }
}
