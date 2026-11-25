using System.Runtime.Intrinsics;

namespace Pragmatic.Imaging.Codecs.Transforms;

/// <summary>
///     Greyscale, brightness and contrast, in place. Each follows the arithmetic of the <c>image</c> crate
///     the native library uses, operation for operation in single precision, so a pixel comes out the
///     same on either engine: brightness and contrast are a function of one channel value, computed once
///     into a 256-entry table; greyscale is a weighted sum, eight pixels at a time where the CPU has the
///     vectors for it.
/// </summary>
public static class PixelAdjustments
{
    private const float RedWeight = 0.299f;
    private const float GreenWeight = 0.587f;
    private const float BlueWeight = 0.114f;

    /// <summary>
    ///     ITU-R BT.601 luma, truncated, written to red, green and blue; alpha is kept. The sum is formed
    ///     red, then green, then blue, with no fused multiply-add, the order the native library uses.
    /// </summary>
    public static void Grayscale(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var pixels = image.Pixels;
        var i = 0;
        if (Vector256.IsHardwareAccelerated)
            i = GrayscaleVector(pixels);
        for (; i < pixels.Length; i += 4)
        {
            var gray = (byte)(RedWeight * pixels[i] + GreenWeight * pixels[i + 1] + BlueWeight * pixels[i + 2]);
            pixels[i] = gray;
            pixels[i + 1] = gray;
            pixels[i + 2] = gray;
        }
    }

    /// <summary>Adds <paramref name="value" /> to red, green and blue, clamped to 0..255; alpha is kept.</summary>
    public static void Brightness(PixelBuffer image, int value)
    {
        ArgumentNullException.ThrowIfNull(image);
        Span<byte> table = stackalloc byte[256];
        for (var c = 0; c < 256; c++)
            table[c] = (byte)Math.Clamp(c + value, 0, 255);
        ApplyToColour(image.Pixels, table);
    }

    /// <summary>
    ///     Scales each channel's distance from mid-grey by <c>((100 + value) / 100)²</c>, clamped and
    ///     truncated. Alpha is scaled too: the native library maps every channel, and this keeps its
    ///     output.
    /// </summary>
    public static void Contrast(PixelBuffer image, float value)
    {
        ArgumentNullException.ThrowIfNull(image);
        var factor = (100f + value) / 100f;
        var percent = factor * factor;
        Span<byte> table = stackalloc byte[256];
        for (var c = 0; c < 256; c++)
        {
            var d = ((c / 255f - 0.5f) * percent + 0.5f) * 255f;
            table[c] = (byte)Math.Clamp(d, 0f, 255f);
        }

        var pixels = image.Pixels;
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = table[pixels[i]];
    }

    private static void ApplyToColour(Span<byte> pixels, ReadOnlySpan<byte> table)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = table[pixels[i]];
            pixels[i + 1] = table[pixels[i + 1]];
            pixels[i + 2] = table[pixels[i + 2]];
        }
    }

    /// <summary>
    ///     Eight pixels a step: the channels are widened to floats, weighted and summed in the scalar
    ///     path's order, truncated, and the grey written back under the original alpha. Returns where
    ///     the scalar path takes over.
    /// </summary>
    private static int GrayscaleVector(Span<byte> pixels)
    {
        var red = Vector256.Create(RedWeight);
        var green = Vector256.Create(GreenWeight);
        var blue = Vector256.Create(BlueWeight);
        var byteMask = Vector256.Create(0xFFu);
        var alphaMask = Vector256.Create(0xFF000000u);
        var i = 0;
        for (; i + 32 <= pixels.Length; i += 32)
        {
            var words = Vector256.Create<uint>(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(pixels.Slice(i, 32)));
            var r = Vector256.ConvertToSingle((words & byteMask).AsInt32());
            var g = Vector256.ConvertToSingle(((words >> 8) & byteMask).AsInt32());
            var b = Vector256.ConvertToSingle(((words >> 16) & byteMask).AsInt32());
            var sum = red * r + green * g + blue * b;
            var gray = Vector256.ConvertToInt32(sum).AsUInt32();
            var result = (words & alphaMask) | gray | (gray << 8) | (gray << 16);
            result.CopyTo(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(pixels.Slice(i, 32)));
        }

        return i;
    }
}
