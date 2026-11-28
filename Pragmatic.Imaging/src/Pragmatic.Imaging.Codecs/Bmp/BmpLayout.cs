namespace Pragmatic.Imaging.Codecs.Bmp;

/// <summary>How a BMP's pixels are stored, once its header is read.</summary>
internal enum BmpLayout
{
    /// <summary>1, 2, 4 or 8 bits of palette index per pixel.</summary>
    Palette,

    /// <summary>8-bit indices, run-length encoded.</summary>
    Rle8,

    /// <summary>4-bit indices, run-length encoded.</summary>
    Rle4,

    /// <summary>24 bits: blue, green, red.</summary>
    Rgb24,

    /// <summary>32 bits: blue, green, red and a byte that is not read.</summary>
    Rgb32,

    /// <summary>16 or 32 bits whose channels the masks place; 16-bit uncompressed is 5-5-5.</summary>
    Bitfields,
}
