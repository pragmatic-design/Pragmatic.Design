namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     A decoded VP8 key frame as its three planes, each a whole number of macroblocks wide and high: Y at
///     full resolution, U and V at half. Only the top-left <see cref="Width" /> x <see cref="Height" /> is
///     the image.
/// </summary>
internal sealed class Vp8Frame
{
    public Vp8Frame(int width, int height)
    {
        Width = width;
        Height = height;
        MacroblocksWide = (width + 15) >> 4;
        MacroblocksHigh = (height + 15) >> 4;
        YStride = MacroblocksWide * 16;
        UvStride = MacroblocksWide * 8;
        Y = new byte[YStride * MacroblocksHigh * 16];
        U = new byte[UvStride * MacroblocksHigh * 8];
        V = new byte[UvStride * MacroblocksHigh * 8];
    }

    public int Width { get; }

    public int Height { get; }

    public int MacroblocksWide { get; }

    public int MacroblocksHigh { get; }

    public int YStride { get; }

    public int UvStride { get; }

    public byte[] Y { get; }

    public byte[] U { get; }

    public byte[] V { get; }
}
