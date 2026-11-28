namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>The colour model the TIFF crate gave an image, from its photometric interpretation and samples.</summary>
internal enum TiffColor
{
    Gray,
    Rgb,
    Rgba,
    Cmyk,
    Cmyka,
    YCbCr,
    Lab,
    Multiband,
}
