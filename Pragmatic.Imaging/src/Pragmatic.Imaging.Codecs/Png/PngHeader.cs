using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>What IHDR says about the image, and the row geometry that follows from it.</summary>
internal readonly record struct PngHeader(int Width, int Height, int BitDepth, PngColorType ColorType, bool Interlaced)
{
    public int Channels => ColorType switch
    {
        PngColorType.Gray => 1,
        PngColorType.Palette => 1,
        PngColorType.GrayAlpha => 2,
        PngColorType.Rgb => 3,
        PngColorType.Rgba => 4,
        _ => throw new ImageDecodeException($"PNG colour type {(int)ColorType} is not one the format defines."),
    };

    public int BitsPerPixel => Channels * BitDepth;

    /// <summary>Bytes of one unfiltered scanline of <paramref name="width" /> pixels, the filter byte excluded.</summary>
    public int ScanlineBytes(int width) => (int)(((long)width * BitsPerPixel + 7) / 8);

    /// <summary>The distance the Sub, Average and Paeth filters look back: a whole pixel, at least one byte.</summary>
    public int FilterStep => Math.Max(1, BitsPerPixel / 8);

    public static PngHeader Parse(ReadOnlySpan<byte> ihdr)
    {
        if (ihdr.Length != 13)
            throw new ImageDecodeException($"PNG IHDR is {ihdr.Length} bytes, not 13.");

        var width = BinaryPrimitives.ReadInt32BigEndian(ihdr);
        var height = BinaryPrimitives.ReadInt32BigEndian(ihdr[4..]);
        if (width <= 0 || height <= 0)
            throw new ImageDecodeException($"PNG dimensions {width}x{height} are not positive.");

        var bitDepth = ihdr[8];
        var colorType = (PngColorType)ihdr[9];
        if (ihdr[10] != 0 || ihdr[11] != 0)
            throw new ImageDecodeException("PNG uses a compression or filter method other than 0.");
        var interlace = ihdr[12] switch
        {
            0 => false,
            1 => true,
            var other => throw new ImageDecodeException($"PNG interlace method {other} is unknown."),
        };

        var depthAllowed = colorType switch
        {
            PngColorType.Gray => bitDepth is 1 or 2 or 4 or 8 or 16,
            PngColorType.Palette => bitDepth is 1 or 2 or 4 or 8,
            PngColorType.Rgb or PngColorType.GrayAlpha or PngColorType.Rgba => bitDepth is 8 or 16,
            _ => false,
        };
        if (!depthAllowed)
            throw new ImageDecodeException($"PNG bit depth {bitDepth} is not allowed for colour type {(int)colorType}.");

        return new PngHeader(width, height, bitDepth, colorType, interlace);
    }
}
