namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>The short constant tables of VP8 (RFC 6386); the long ones are in the other part of this class.</summary>
internal static partial class Vp8Tables
{
    public const int NumTypes = 4;
    public const int NumBands = 8;
    public const int NumContexts = 3;
    public const int NumProbabilities = 11;

    /// <summary>The order coefficients come in, as raster positions in the 4 x 4 block.</summary>
    public static ReadOnlySpan<byte> Zigzag => [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15];

    /// <summary>The probability band of each coefficient position, with a 17th entry for the position after the last.</summary>
    public static ReadOnlySpan<byte> Bands => [0, 1, 2, 3, 6, 4, 5, 6, 6, 6, 6, 6, 6, 6, 6, 7, 0];

    /// <summary>The extra bits of the token categories 3 to 6, each list ending at zero.</summary>
    public static ReadOnlySpan<byte> Category3 => [173, 148, 140, 0];

    public static ReadOnlySpan<byte> Category4 => [176, 155, 140, 135, 0];

    public static ReadOnlySpan<byte> Category5 => [180, 157, 141, 134, 130, 0];

    public static ReadOnlySpan<byte> Category6 => [254, 254, 243, 230, 196, 177, 153, 140, 133, 130, 129, 0];

    /// <summary>
    ///     The category of a level of 11 or more, 0 for category 3 to 3 for category 6, with its extra bits'
    ///     probabilities, how many there are, and the first level it codes.
    /// </summary>
    public static ReadOnlySpan<byte> LargeCategory(int level, out int category, out int bits, out int first)
    {
        category = level < 3 + (8 << 1) ? 0 : level < 3 + (8 << 2) ? 1 : level < 3 + (8 << 3) ? 2 : 3;
        first = 3 + (8 << category);
        bits = category == 3 ? 11 : category + 3;
        return category switch
        {
            0 => Category3,
            1 => Category4,
            2 => Category5,
            _ => Category6,
        };
    }

    /// <summary>The tree of the 4 x 4 intra modes in key frames (RFC 6386, section 11.2), in RFC numbering.</summary>
    public static ReadOnlySpan<sbyte> BModeTree =>
    [
        -Vp8Modes.BDc, 2, -Vp8Modes.BTm, 4, -Vp8Modes.BVe, 6, 8, 12, -Vp8Modes.BHe, 10, -Vp8Modes.BRd, -Vp8Modes.BVr,
        -Vp8Modes.BLd, 14, -Vp8Modes.BVl, 16, -Vp8Modes.BHd, -Vp8Modes.BHu,
    ];
}
