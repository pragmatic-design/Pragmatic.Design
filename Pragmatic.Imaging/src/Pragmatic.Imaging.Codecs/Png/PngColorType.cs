namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>The colour type byte of IHDR.</summary>
internal enum PngColorType : byte
{
    Gray = 0,
    Rgb = 2,
    Palette = 3,
    GrayAlpha = 4,
    Rgba = 6,
}
