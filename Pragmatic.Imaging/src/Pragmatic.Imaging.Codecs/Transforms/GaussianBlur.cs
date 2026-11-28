using System.Buffers;
using System.Numerics;

namespace Pragmatic.Imaging.Codecs.Transforms;

/// <summary>
///     A separable Gaussian blur and the unsharp mask built on it, with the kernel the <c>image</c> crate
///     builds for the same sigma (its size and its weights), edges clamped, horizontal pass first, every
///     channel alpha included, rounded to nearest at the end. Summation order differs from the crate's,
///     so a channel can land one unit away from the native library's on a rounding boundary. An image
///     with any translucent pixel is filtered premultiplied, so the colour of a pixel nobody sees does not
///     leak into its visible neighbours; an opaque one, where premultiplying changes nothing, takes the
///     straight path, the native library's arithmetic.
/// </summary>
/// <remarks>
///     The crate converts the whole image to floats twice over (16 bytes a pixel each). Here the
///     horizontally filtered rows sit in a ring of kernel-height rows, filled as the vertical pass walks
///     down, so the extra memory is the ring, not the image.
/// </remarks>
public static class GaussianBlur
{
    /// <summary>
    ///     Blurs with standard deviation <paramref name="sigma" /> pixels. Zero means the crate's default
    ///     of 0.8, as the native library's <c>blur</c> treats it.
    /// </summary>
    public static PixelBuffer Blur(PixelBuffer source, float sigma)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Convolve(source, Kernel(sigma == 0f ? 0.8f : sigma), !PixelRows.IsOpaque(source));
    }

    /// <summary>
    ///     Sharpens by adding back the difference from a blur of <paramref name="sigma" />, on each
    ///     channel whose difference exceeds <paramref name="threshold" />, clamped to 0..255.
    /// </summary>
    public static PixelBuffer Unsharpen(PixelBuffer source, float sigma, int threshold)
    {
        ArgumentNullException.ThrowIfNull(source);
        var translucent = !PixelRows.IsOpaque(source);
        var result = Convolve(source, Kernel(sigma), translucent);
        if (translucent)
        {
            UnsharpenPremultiplied(source, result, threshold);
            return result;
        }

        if (source.Format != PixelFormat.Rgba8)
        {
            UnsharpenAnyFormat(source, result, threshold);
            return result;
        }

        var original = source.Pixels;
        var blurred = result.Pixels;
        for (var i = 0; i < original.Length; i++)
        {
            var diff = original[i] - blurred[i];
            blurred[i] = Math.Abs(diff) > threshold ? (byte)Math.Clamp(original[i] + diff, 0, 255) : original[i];
        }

        return result;
    }

    // The same rule on any other format, the threshold in 8-bit steps scaled to the format's.
    private static void UnsharpenAnyFormat(PixelBuffer source, PixelBuffer result, int threshold)
    {
        var scale = PixelRows.Scale(source.Format);
        var limit = threshold * scale / 255f;
        var pool = ArrayPool<float>.Shared;
        var original = pool.Rent(source.Width * 4);
        var blurred = pool.Rent(source.Width * 4);
        try
        {
            for (var y = 0; y < source.Height; y++)
            {
                PixelRows.Widen(source.Format, source.Row(y), original);
                PixelRows.Widen(source.Format, result.Row(y), blurred);
                for (var i = 0; i < source.Width * 4; i++)
                {
                    var diff = original[i] - blurred[i];
                    blurred[i] = MathF.Abs(diff) > limit ? Math.Clamp(original[i] + diff, 0f, scale) : original[i];
                }

                PixelRows.Narrow(source.Format, blurred.AsSpan(0, source.Width * 4), result.Row(y));
            }
        }
        finally
        {
            pool.Return(original);
            pool.Return(blurred);
        }
    }

    // The rule on premultiplied pixels, the threshold in 8-bit steps scaled to the format's. Colour and alpha are
    // sharpened alike, so their ratio, the colour that shows, holds; the colour is divided back by the alpha before
    // the alpha is clamped, or an alpha pushed past opaque would brighten it.
    private static void UnsharpenPremultiplied(PixelBuffer source, PixelBuffer result, int threshold)
    {
        var scale = PixelRows.Scale(source.Format);
        var limit = threshold * scale / 255f;
        var pool = ArrayPool<float>.Shared;
        var original = pool.Rent(source.Width * 4);
        var blurred = pool.Rent(source.Width * 4);
        try
        {
            var row = source.Width * 4;
            for (var y = 0; y < source.Height; y++)
            {
                PixelRows.Widen(source.Format, source.Row(y), original);
                PixelRows.Widen(source.Format, result.Row(y), blurred);
                PixelRows.Premultiply(original.AsSpan(0, row), scale);
                PixelRows.Premultiply(blurred.AsSpan(0, row), scale);
                for (var i = 0; i < row; i++)
                {
                    var diff = original[i] - blurred[i];
                    blurred[i] = MathF.Abs(diff) > limit ? original[i] + diff : original[i];
                }

                PixelRows.Unpremultiply(blurred.AsSpan(0, row), scale);
                PixelRows.Narrow(source.Format, blurred.AsSpan(0, row), result.Row(y));
            }
        }
        finally
        {
            pool.Return(original);
            pool.Return(blurred);
        }
    }

    /// <summary>The crate's kernel: odd size from sigma (at least 3), Gaussian weights normalised to one.</summary>
    internal static float[] Kernel(float sigma)
    {
        if (!float.IsNormal(sigma) || sigma < 0f)
            throw new ArgumentOutOfRangeException(nameof(sigma), sigma, "Sigma must be a positive, finite number.");

        var size = (int)MathF.Max(((sigma - 0.8f) / 0.3f + 1f) * 2f + 1f, 3f);
        if (size % 2 == 0)
            size++;

        var kernel = new float[size];
        var scale = 1f / (MathF.Sqrt(2f * MathF.PI) * sigma);
        var mean = (float)(size / 2);
        var sum = 0f;
        for (var x = 0; x < size; x++)
        {
            var t = (x - mean) / sigma;
            kernel[x] = MathF.Exp(-0.5f * MathF.Pow(t, 2f)) * scale;
            sum += kernel[x];
        }

        if (sum != 0f)
        {
            var normalise = 1f / sum;
            for (var x = 0; x < size; x++)
                kernel[x] *= normalise;
        }

        return kernel;
    }

    private static PixelBuffer Convolve(PixelBuffer source, float[] kernel, bool premultiply)
    {
        var scale = PixelRows.Scale(source.Format);
        var (width, height) = (source.Width, source.Height);
        var size = kernel.Length;
        var half = size / 2;
        var rowFloats = width * 4;
        var pool = ArrayPool<float>.Shared;
        var padded = pool.Rent((width + 2 * half) * 4);
        var ring = pool.Rent(size * rowFloats);
        var sum = pool.Rent(rowFloats);
        var result = new PixelBuffer(width, height, source.Format);
        try
        {
            var loaded = -1;
            for (var y = 0; y < height; y++)
            {
                for (; loaded < Math.Min(height - 1, y + half); loaded++)
                    FilterRow(source.Format, source.Row(loaded + 1), width, padded, kernel, ring.AsSpan((loaded + 1) % size * rowFloats, rowFloats), premultiply);

                var acc = sum.AsSpan(0, rowFloats);
                acc.Clear();
                for (var k = 0; k < size; k++)
                {
                    var sy = Math.Clamp(y + k - half, 0, height - 1);
                    MultiplyAdd(ring.AsSpan(sy % size * rowFloats, rowFloats), kernel[k], acc);
                }

                if (premultiply)
                    PixelRows.Unpremultiply(acc, scale);

                if (source.Format == PixelFormat.Rgba8)
                {
                    // The crate's rounding, half away from zero: adding a half and truncating rounds up a sum just
                    // below a half whose low bits the addition drops.
                    var output = result.Row(y);
                    for (var i = 0; i < rowFloats; i++)
                        output[i] = (byte)Math.Clamp(MathF.Round(acc[i], MidpointRounding.AwayFromZero), 0f, 255f);
                }
                else
                {
                    PixelRows.Narrow(source.Format, acc, result.Row(y));
                }
            }

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
        finally
        {
            pool.Return(padded);
            pool.Return(ring);
            pool.Return(sum);
        }
    }

    /// <summary>
    ///     One source row, widened to floats in place inside its padding (premultiplied when asked), its edge pixels
    ///     repeated, convolved horizontally.
    /// </summary>
    private static void FilterRow(PixelFormat format, ReadOnlySpan<byte> row, int width, float[] padded, float[] kernel, Span<float> output, bool premultiply)
    {
        var half = kernel.Length / 2;
        var line = padded.AsSpan(0, (width + 2 * half) * 4);
        var pixels = line.Slice(half * 4, width * 4);
        PixelRows.Widen(format, row, pixels);
        if (premultiply)
            PixelRows.Premultiply(pixels, PixelRows.Scale(format));
        var first = line.Slice(half * 4, 4);
        var last = line.Slice((half + width - 1) * 4, 4);
        for (var p = 0; p < half; p++)
        {
            first.CopyTo(line[(p * 4)..]);
            last.CopyTo(line[((half + width + p) * 4)..]);
        }

        output.Clear();
        for (var k = 0; k < kernel.Length; k++)
            MultiplyAdd(line.Slice(k * 4, output.Length), kernel[k], output);
    }

    /// <summary><paramref name="accumulator" /> += <paramref name="weight" /> × <paramref name="values" />, a vector at a time.</summary>
    private static void MultiplyAdd(ReadOnlySpan<float> values, float weight, Span<float> accumulator)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var w = new Vector<float>(weight);
            var count = Vector<float>.Count;
            for (; i + count <= accumulator.Length; i += count)
                (new Vector<float>(accumulator[i..]) + w * new Vector<float>(values[i..])).CopyTo(accumulator[i..]);
        }

        for (; i < accumulator.Length; i++)
            accumulator[i] += weight * values[i];
    }
}
