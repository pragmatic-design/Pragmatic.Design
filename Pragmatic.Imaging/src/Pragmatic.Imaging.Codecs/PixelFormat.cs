namespace Pragmatic.Imaging.Codecs;

/// <summary>
///     How a <see cref="PixelBuffer" /> lays out a pixel: its channels, in this order, and the width of each. Samples of
///     16 bits are little-endian unsigned, 0 to 65,535; floats are IEEE single precision, 0 to 1 within the gamut.
///     Alpha is straight, never premultiplied, in every format that has it.
/// </summary>
public enum PixelFormat
{
    /// <summary>Red, green, blue and alpha, a byte each: the default, and what every codec reads and writes.</summary>
    Rgba8,

    /// <summary>Red, green, blue and alpha, 16 bits each.</summary>
    Rgba16,

    /// <summary>Red, green, blue and alpha, a 32-bit float each.</summary>
    RgbaFloat,

    /// <summary>Red, green and blue, a byte each.</summary>
    Rgb8,

    /// <summary>Red, green and blue, 16 bits each.</summary>
    Rgb16,

    /// <summary>One grey sample, a byte.</summary>
    Gray8,

    /// <summary>One grey sample, 16 bits.</summary>
    Gray16,
}
