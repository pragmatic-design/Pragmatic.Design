using System.Buffers;
using System.Numerics;

namespace Pragmatic.Imaging.Codecs.Transforms;

/// <summary>
///     A separable Gaussian blur and the unsharp mask built on it, with the kernel the <c>image</c> crate
///     builds for the same sigma (its size and its weights), edges clamped, horizontal pass first, every
///     channel alpha included, rounded to nearest at the end. Summation order differs from the crate's,
///     so a channel can land one unit away from the native library's on a rounding boundary.
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
        return Convolve(source, Kernel(sigma == 0f ? 0.8f : sigma));
    }

    /// <summary>
    ///     Sharpens by adding back the difference from a blur of <paramref name="sigma" />, on each
    ///     channel whose difference exceeds <paramref name="threshold" />, clamped to 0..255.
    /// </summary>
    public static PixelBuffer Unsharpen(PixelBuffer source, float sigma, int threshold)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = Convolve(source, Kernel(sigma));
        var original = source.Pixels;
        var blurred = result.Pixels;
        for (var i = 0; i < original.Length; i++)
        {
            var diff = original[i] - blurred[i];
            blurred[i] = Math.Abs(diff) > threshold ? (byte)Math.Clamp(original[i] + diff, 0, 255) : original[i];
        }

        return result;
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

    private static PixelBuffer Convolve(PixelBuffer source, float[] kernel)
    {
        var (width, height) = (source.Width, source.Height);
        var size = kernel.Length;
        var half = size / 2;
        var rowFloats = width * 4;
        var pool = ArrayPool<float>.Shared;
        var padded = pool.Rent((width + 2 * half) * 4);
        var ring = pool.Rent(size * rowFloats);
        var sum = pool.Rent(rowFloats);
        var result = new PixelBuffer(width, height);
        try
        {
            var loaded = -1;
            for (var y = 0; y < height; y++)
            {
                for (; loaded < Math.Min(height - 1, y + half); loaded++)
                    FilterRow(source.Row(loaded + 1), padded, kernel, ring.AsSpan((loaded + 1) % size * rowFloats, rowFloats));

                var acc = sum.AsSpan(0, rowFloats);
                acc.Clear();
                for (var k = 0; k < size; k++)
                {
                    var sy = Math.Clamp(y + k - half, 0, height - 1);
                    MultiplyAdd(ring.AsSpan(sy % size * rowFloats, rowFloats), kernel[k], acc);
                }

                var output = result.Row(y);
                for (var i = 0; i < rowFloats; i++)
                    output[i] = (byte)Math.Clamp(MathF.Round(acc[i], MidpointRounding.AwayFromZero), 0f, 255f);
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

    /// <summary>One source row, widened to floats with its edge pixels repeated, convolved horizontally.</summary>
    private static void FilterRow(ReadOnlySpan<byte> row, float[] padded, float[] kernel, Span<float> output)
    {
        var half = kernel.Length / 2;
        var width = row.Length / 4;
        var line = padded.AsSpan(0, (width + 2 * half) * 4);
        for (var p = 0; p < width + 2 * half; p++)
        {
            var sx = Math.Clamp(p - half, 0, width - 1) * 4;
            line[p * 4] = row[sx];
            line[p * 4 + 1] = row[sx + 1];
            line[p * 4 + 2] = row[sx + 2];
            line[p * 4 + 3] = row[sx + 3];
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
