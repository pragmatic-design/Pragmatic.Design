namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>
///     Turns one raw scanline, in the image's own colour type and bit depth, into 8-bit RGBA. Sixteen-bit
///     samples keep their high byte; samples narrower than 8 bits are scaled to the full range.
/// </summary>
internal static class PngScanlineConverter
{
    public static void Convert(in PngHeader header, PngPalette palette, ReadOnlySpan<byte> scanline, Span<byte> rgba, int pixels)
    {
        switch (header.ColorType)
        {
            case PngColorType.Rgba:
                ConvertRgba(header.BitDepth, scanline, rgba, pixels);
                return;
            case PngColorType.Rgb:
                ConvertRgb(header.BitDepth, palette, scanline, rgba, pixels);
                return;
            case PngColorType.Gray:
                ConvertGray(header.BitDepth, palette, scanline, rgba, pixels);
                return;
            case PngColorType.GrayAlpha:
                ConvertGrayAlpha(header.BitDepth, scanline, rgba, pixels);
                return;
            case PngColorType.Palette:
                ConvertPalette(header.BitDepth, palette, scanline, rgba, pixels);
                return;
            default:
                throw new ImageDecodeException($"PNG colour type {(int)header.ColorType} is not one the format defines.");
        }
    }

    private static void ConvertRgba(int bitDepth, ReadOnlySpan<byte> scanline, Span<byte> rgba, int pixels)
    {
        if (bitDepth == 8)
        {
            scanline[..(pixels * 4)].CopyTo(rgba);
            return;
        }

        for (int p = 0, s = 0, o = 0; p < pixels; p++, s += 8, o += 4)
        {
            rgba[o] = scanline[s];
            rgba[o + 1] = scanline[s + 2];
            rgba[o + 2] = scanline[s + 4];
            rgba[o + 3] = scanline[s + 6];
        }
    }

    private static void ConvertRgb(int bitDepth, PngPalette palette, ReadOnlySpan<byte> scanline, Span<byte> rgba, int pixels)
    {
        var hasKey = palette.TransparentRed >= 0;
        if (bitDepth == 8)
        {
            for (int p = 0, s = 0, o = 0; p < pixels; p++, s += 3, o += 4)
            {
                var r = scanline[s];
                var g = scanline[s + 1];
                var b = scanline[s + 2];
                rgba[o] = r;
                rgba[o + 1] = g;
                rgba[o + 2] = b;
                rgba[o + 3] = hasKey && r == palette.TransparentRed && g == palette.TransparentGreen && b == palette.TransparentBlue ? (byte)0 : (byte)255;
            }

            return;
        }

        for (int p = 0, s = 0, o = 0; p < pixels; p++, s += 6, o += 4)
        {
            var r = (scanline[s] << 8) | scanline[s + 1];
            var g = (scanline[s + 2] << 8) | scanline[s + 3];
            var b = (scanline[s + 4] << 8) | scanline[s + 5];
            rgba[o] = scanline[s];
            rgba[o + 1] = scanline[s + 2];
            rgba[o + 2] = scanline[s + 4];
            rgba[o + 3] = hasKey && r == palette.TransparentRed && g == palette.TransparentGreen && b == palette.TransparentBlue ? (byte)0 : (byte)255;
        }
    }

    private static void ConvertGray(int bitDepth, PngPalette palette, ReadOnlySpan<byte> scanline, Span<byte> rgba, int pixels)
    {
        var key = palette.TransparentGray;
        if (bitDepth == 16)
        {
            for (int p = 0, s = 0, o = 0; p < pixels; p++, s += 2, o += 4)
            {
                var sample = (scanline[s] << 8) | scanline[s + 1];
                var v = scanline[s];
                rgba[o] = v;
                rgba[o + 1] = v;
                rgba[o + 2] = v;
                rgba[o + 3] = sample == key ? (byte)0 : (byte)255;
            }

            return;
        }

        // 1, 2, 4 or 8 bits: scale the sample to 0..255 (0xFF / max is exact for these depths).
        var max = (1 << bitDepth) - 1;
        var scale = 255 / max;
        for (int p = 0, o = 0; p < pixels; p++, o += 4)
        {
            var sample = Sample(scanline, p, bitDepth);
            var v = (byte)(sample * scale);
            rgba[o] = v;
            rgba[o + 1] = v;
            rgba[o + 2] = v;
            rgba[o + 3] = sample == key ? (byte)0 : (byte)255;
        }
    }

    private static void ConvertGrayAlpha(int bitDepth, ReadOnlySpan<byte> scanline, Span<byte> rgba, int pixels)
    {
        var sampleBytes = bitDepth / 8;
        for (int p = 0, s = 0, o = 0; p < pixels; p++, s += 2 * sampleBytes, o += 4)
        {
            var v = scanline[s];
            rgba[o] = v;
            rgba[o + 1] = v;
            rgba[o + 2] = v;
            rgba[o + 3] = scanline[s + sampleBytes];
        }
    }

    private static void ConvertPalette(int bitDepth, PngPalette palette, ReadOnlySpan<byte> scanline, Span<byte> rgba, int pixels)
    {
        var entries = palette.Entries;
        var count = palette.Count;
        for (int p = 0, o = 0; p < pixels; p++, o += 4)
        {
            var index = Sample(scanline, p, bitDepth);
            if (index >= count)
                throw new ImageDecodeException($"PNG pixel refers to palette entry {index}, and the palette has {count}.");
            entries.AsSpan(index * 4, 4).CopyTo(rgba.Slice(o, 4));
        }
    }

    /// <summary>The <paramref name="index" />th sample of a packed scanline of 1, 2, 4 or 8-bit samples.</summary>
    private static int Sample(ReadOnlySpan<byte> scanline, int index, int bitDepth)
    {
        if (bitDepth == 8)
            return scanline[index];
        var bit = index * bitDepth;
        var shift = 8 - bitDepth - (bit & 7);
        return (scanline[bit >> 3] >> shift) & ((1 << bitDepth) - 1);
    }
}
