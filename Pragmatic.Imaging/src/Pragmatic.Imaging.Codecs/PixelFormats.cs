namespace Pragmatic.Imaging.Codecs;

/// <summary>What each <see cref="PixelFormat" /> holds: its channels, their width, the bytes of a pixel.</summary>
public static class PixelFormats
{
    public static int BytesPerPixel(this PixelFormat format) => format switch
    {
        PixelFormat.Rgba8 => 4,
        PixelFormat.Rgba16 => 8,
        PixelFormat.RgbaFloat => 16,
        PixelFormat.Rgb8 => 3,
        PixelFormat.Rgb16 => 6,
        PixelFormat.Gray8 => 1,
        PixelFormat.Gray16 => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a pixel format."),
    };

    public static int Channels(this PixelFormat format) => format switch
    {
        PixelFormat.Rgba8 or PixelFormat.Rgba16 or PixelFormat.RgbaFloat => 4,
        PixelFormat.Rgb8 or PixelFormat.Rgb16 => 3,
        PixelFormat.Gray8 or PixelFormat.Gray16 => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a pixel format."),
    };

    /// <summary>The bits of one channel: 8, 16, or 32 for the float format.</summary>
    public static int BitsPerChannel(this PixelFormat format) => format.BytesPerPixel() / format.Channels() * 8;

    public static bool HasAlpha(this PixelFormat format) => format.Channels() == 4;

    public static bool IsGray(this PixelFormat format) => format.Channels() == 1;
}
