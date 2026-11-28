namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     The pixel types the image crate 0.25 reads a TIFF into, the only ones the native library decoded: a sample
///     layout and its width. CMYK becomes RGB, and one-bit grey becomes eight-bit grey, after the samples are read.
/// </summary>
internal enum TiffPixelFormat
{
    Gray1,
    Gray8,
    Gray16,
    Rgb8,
    Rgb16,
    Rgba8,
    Rgba16,
    Cmyk8,
    Cmyk16,
    Rgb32Float,
    Rgba32Float,
}
