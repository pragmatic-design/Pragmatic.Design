namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>
///     What PLTE and tRNS contribute: a palette as RGBA entries (alpha from tRNS, opaque where it says
///     nothing), or, for grey and RGB images, the one sample value that is transparent.
/// </summary>
internal sealed class PngPalette
{
    /// <summary>RGBA, four bytes per entry, up to 256 entries.</summary>
    public byte[] Entries { get; private set; } = [];

    public int Count => Entries.Length / 4;

    /// <summary>For grey images: the sample value tRNS marks transparent, or -1.</summary>
    public int TransparentGray { get; private set; } = -1;

    /// <summary>For RGB images: the (R, G, B) sample values tRNS marks transparent, or -1 each.</summary>
    public int TransparentRed { get; private set; } = -1;
    public int TransparentGreen { get; private set; } = -1;
    public int TransparentBlue { get; private set; } = -1;

    public void ReadPlte(ReadOnlySpan<byte> data)
    {
        if (data.Length % 3 != 0 || data.Length == 0 || data.Length > 256 * 3)
            throw new ImageDecodeException($"PNG PLTE of {data.Length} bytes is not 1 to 256 RGB entries.");

        var entries = new byte[data.Length / 3 * 4];
        for (int i = 0, o = 0; i < data.Length; i += 3, o += 4)
        {
            entries[o] = data[i];
            entries[o + 1] = data[i + 1];
            entries[o + 2] = data[i + 2];
            entries[o + 3] = 255;
        }

        Entries = entries;
    }

    public void ReadTrns(PngColorType colorType, ReadOnlySpan<byte> data)
    {
        switch (colorType)
        {
            case PngColorType.Palette:
                if (data.Length > Count)
                    throw new ImageDecodeException("PNG tRNS has more entries than the palette.");
                for (var i = 0; i < data.Length; i++)
                    Entries[i * 4 + 3] = data[i];
                return;
            case PngColorType.Gray:
                if (data.Length != 2)
                    throw new ImageDecodeException("PNG tRNS for a grey image is not 2 bytes.");
                TransparentGray = (data[0] << 8) | data[1];
                return;
            case PngColorType.Rgb:
                if (data.Length != 6)
                    throw new ImageDecodeException("PNG tRNS for an RGB image is not 6 bytes.");
                TransparentRed = (data[0] << 8) | data[1];
                TransparentGreen = (data[2] << 8) | data[3];
                TransparentBlue = (data[4] << 8) | data[5];
                return;
            default:
                throw new ImageDecodeException("PNG tRNS is not allowed on an image with an alpha channel.");
        }
    }
}
