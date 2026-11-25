namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     One transform of a lossless image, as read from its stream: which one, the block size of its data
///     (or, for colour indexing, how many pixels share a byte), the data, and the width of the image it
///     produces when inverted.
/// </summary>
/// <param name="Kind">0 predictor, 1 cross colour, 2 subtract green, 3 colour indexing (RFC 9649, section 4).</param>
/// <param name="Bits">The size bits of the transform's sub-image, or for colour indexing the packing bits.</param>
/// <param name="Data">The sub-image (predictor, cross colour) or the colour table; empty for subtract green.</param>
/// <param name="Width">The width of the image the inverse transform writes.</param>
internal sealed record Vp8LTransform(int Kind, int Bits, uint[] Data, int Width)
{
    public const int Predictor = 0;
    public const int CrossColor = 1;
    public const int SubtractGreen = 2;
    public const int ColorIndexing = 3;
}
