using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>
///     A 16-bit scanline into a buffer that keeps the depth: the big-endian samples of the file become the little-endian
///     ones of <see cref="PixelFormat.Rgba16" />, <see cref="PixelFormat.Rgb16" /> or <see cref="PixelFormat.Gray16" />,
///     every bit kept. A colour key from <c>tRNS</c> needs an alpha channel, so a keyed image decodes to RGBA.
/// </summary>
internal static class PngWideScanlineConverter
{
    /// <summary>The format a 16-bit image decodes to; every shallower one decodes to <see cref="PixelFormat.Rgba8" />.</summary>
    public static PixelFormat FormatOf(in PngHeader header, PngPalette palette)
    {
        if (header.BitDepth != 16)
            return PixelFormat.Rgba8;
        return header.ColorType switch
        {
            PngColorType.Gray when palette.TransparentGray < 0 => PixelFormat.Gray16,
            PngColorType.Rgb when palette.TransparentRed < 0 => PixelFormat.Rgb16,
            _ => PixelFormat.Rgba16,
        };
    }

    public static void Convert(in PngHeader header, PngPalette palette, ReadOnlySpan<byte> scanline, Span<byte> row, int pixels, PixelFormat format)
    {
        var channels = header.ColorType switch
        {
            PngColorType.Gray => 1,
            PngColorType.GrayAlpha => 2,
            PngColorType.Rgb => 3,
            PngColorType.Rgba => 4,
            _ => throw new ImageDecodeException($"PNG colour type {(int)header.ColorType} has no 16-bit form."),
        };

        if (format != PixelFormat.Rgba16)
        {
            // Gray16 from grey, Rgb16 from RGB: the same samples, each one's two bytes swapped.
            for (var i = 0; i < pixels * channels; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(row[(i * 2)..], BinaryPrimitives.ReadUInt16BigEndian(scanline[(i * 2)..]));
            return;
        }

        for (var p = 0; p < pixels; p++)
        {
            var s = scanline[(p * channels * 2)..];
            ushort r, g, b, a;
            switch (header.ColorType)
            {
                case PngColorType.Gray:
                    r = g = b = BinaryPrimitives.ReadUInt16BigEndian(s);
                    a = r == palette.TransparentGray ? (ushort)0 : ushort.MaxValue;
                    break;
                case PngColorType.GrayAlpha:
                    r = g = b = BinaryPrimitives.ReadUInt16BigEndian(s);
                    a = BinaryPrimitives.ReadUInt16BigEndian(s[2..]);
                    break;
                case PngColorType.Rgb:
                    r = BinaryPrimitives.ReadUInt16BigEndian(s);
                    g = BinaryPrimitives.ReadUInt16BigEndian(s[2..]);
                    b = BinaryPrimitives.ReadUInt16BigEndian(s[4..]);
                    a = r == palette.TransparentRed && g == palette.TransparentGreen && b == palette.TransparentBlue ? (ushort)0 : ushort.MaxValue;
                    break;
                default:
                    r = BinaryPrimitives.ReadUInt16BigEndian(s);
                    g = BinaryPrimitives.ReadUInt16BigEndian(s[2..]);
                    b = BinaryPrimitives.ReadUInt16BigEndian(s[4..]);
                    a = BinaryPrimitives.ReadUInt16BigEndian(s[6..]);
                    break;
            }

            var o = row[(p * 8)..];
            BinaryPrimitives.WriteUInt16LittleEndian(o, r);
            BinaryPrimitives.WriteUInt16LittleEndian(o[2..], g);
            BinaryPrimitives.WriteUInt16LittleEndian(o[4..], b);
            BinaryPrimitives.WriteUInt16LittleEndian(o[6..], a);
        }
    }
}
