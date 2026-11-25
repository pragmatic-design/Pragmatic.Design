// ReSharper disable once CheckNamespace
namespace Pragmatic.Testing.Visual;

/// <summary>
///     An image as the comparisons read it: 8-bit RGBA, unpremultiplied, rows top to bottom, four bytes per
///     pixel and no padding. Whatever decoded it, the comparison sees only these bytes.
/// </summary>
public sealed class RgbaImage
{
    public RgbaImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != (long)width * height * 4)
            throw new ArgumentException($"{width} x {height} RGBA needs {(long)width * height * 4} bytes, not {pixels.Length}.", nameof(pixels));

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    /// <summary>Three bytes per pixel in, alpha 255 out.</summary>
    public static RgbaImage FromRgb(int width, int height, ReadOnlySpan<byte> rgb)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0, o = 0; o < pixels.Length; i += 3, o += 4)
        {
            pixels[o] = rgb[i];
            pixels[o + 1] = rgb[i + 1];
            pixels[o + 2] = rgb[i + 2];
            pixels[o + 3] = 255;
        }

        return new RgbaImage(width, height, pixels);
    }

    internal static void RequireSameSize(RgbaImage actual, RgbaImage expected)
    {
        if (actual.Width != expected.Width || actual.Height != expected.Height)
            throw new ArgumentException($"The images differ in size: {actual.Width} x {actual.Height} against {expected.Width} x {expected.Height}.");
    }
}
