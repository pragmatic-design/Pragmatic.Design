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
        if (image.Format != PixelFormat.Rgba8)
        {
            if (!image.Format.IsGray())
                AnyFormat(image, static (pixel, scale) =>
                {
                    var gray = RedWeight * pixel[0] + GreenWeight * pixel[1] + BlueWeight * pixel[2];
                    pixel[0] = pixel[1] = pixel[2] = Truncated(gray, scale);
                });
            return;
        }

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
        if (image.Format != PixelFormat.Rgba8)
        {
            BrightnessAnyFormat(image, value);
            return;
        }

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
        if (image.Format != PixelFormat.Rgba8)
        {
            ContrastAnyFormat(image, percent);
            return;
        }

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

    // The lambdas capture, so they live in methods of their own: in the caller the closure would be allocated on entry,
    // on the 8-bit path too, which allocates nothing.
    private static void BrightnessAnyFormat(PixelBuffer image, int value) => AnyFormat(image, (pixel, scale) =>
    {
        // The value is in 8-bit steps, whatever the format.
        for (var c = 0; c < 3; c++)
            pixel[c] = Math.Clamp(pixel[c] + value * scale / 255f, 0f, scale);
    });

    private static void ContrastAnyFormat(PixelBuffer image, float percent) => AnyFormat(image, (pixel, scale) =>
    {
        for (var c = 0; c < 4; c++)
            pixel[c] = Truncated(Math.Clamp(((pixel[c] / scale - 0.5f) * percent + 0.5f) * scale, 0f, scale), scale);
    });

    // A pixel as four floats in the format's scale, changed in place.
    private delegate void PixelChange(Span<float> pixel, float scale);

    // Any format but 8-bit RGBA: each row widened to floats, each pixel changed, the row narrowed back. A format
    // without alpha gains none: narrowing drops it again.
    private static void AnyFormat(PixelBuffer image, PixelChange change)
    {
        var scale = PixelRows.Scale(image.Format);
        var floats = System.Buffers.ArrayPool<float>.Shared.Rent(image.Width * 4);
        try
        {
            var row = floats.AsSpan(0, image.Width * 4);
            for (var y = 0; y < image.Height; y++)
            {
                PixelRows.Widen(image.Format, image.Row(y), row);
                for (var x = 0; x < image.Width; x++)
                    change(row.Slice(x * 4, 4), scale);
                PixelRows.Narrow(image.Format, row, image.Row(y));
            }
        }
        finally
        {
            System.Buffers.ArrayPool<float>.Shared.Return(floats);
        }
    }

    // Integer formats truncate as the 8-bit tables do; floats keep the value.
    private static float Truncated(float value, float scale) => scale == 1f ? value : MathF.Floor(value);

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
