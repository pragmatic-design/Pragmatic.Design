namespace Pragmatic.Imaging;

/// <summary>
/// Supported image formats. Values match the Rust FormatCode enum.
/// </summary>
public enum ImageFormat
{
    /// <summary>Format could not be determined from the image header.</summary>
    Unknown = 0,

    /// <summary>Portable Network Graphics — lossless, supports transparency.</summary>
    Png = 1,

    /// <summary>JPEG — lossy compression, no transparency. Quality 1–100.</summary>
    Jpeg = 2,

    /// <summary>WebP — modern format with both lossy and lossless modes.</summary>
    WebP = 3,

    /// <summary>AVIF — AV1-based image format; high compression, HDR support.</summary>
    Avif = 4,

    /// <summary>GIF — indexed-color, supports animation (encode writes first frame only).</summary>
    Gif = 5,

    /// <summary>BMP — uncompressed Windows bitmap.</summary>
    Bmp = 6,

    /// <summary>TIFF — flexible multi-page format; lossless.</summary>
    Tiff = 7
}
