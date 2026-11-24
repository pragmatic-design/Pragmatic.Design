using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Pragmatic.Imaging.Codecs.Transforms;

/// <summary>
///     Resizes RGBA pixels in two separable passes, vertical first: each destination row is a weighted
///     sum of whole source rows (a vector at a time, the pass that vectorises best), then that one row
///     is resampled horizontally into the destination width. Source rows are converted to floats once,
///     into a window that slides down the image. The weights are computed once per axis. Alpha is
///     resampled like a colour, not premultiplied: what the native library does today.
/// </summary>
public static class Resampler
{
    /// <summary>
    ///     Resizes <paramref name="source" /> to <paramref name="width" /> × <paramref name="height" />.
    ///     A large reduction is first halved, in 2×2 means, while the image is still at least twice the
    ///     target on both sides; the kernel then covers a factor below two. <paramref name="exact" /> runs
    ///     the kernel over the whole reduction instead: closer to the ideal filter, several times slower.
    /// </summary>
    public static PixelBuffer Resize(PixelBuffer source, int width, int height, ResizeFilter filter = ResizeFilter.Lanczos3, bool exact = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        if (filter == ResizeFilter.Nearest)
            return Nearest(source, width, height);

        if (!exact && source.Width >= width * 2 && source.Height >= height * 2)
        {
            var reduced = BoxReduce.Half(source);
            try
            {
                while (reduced.Width >= width * 2 && reduced.Height >= height * 2)
                {
                    var next = BoxReduce.Half(reduced);
                    reduced.Dispose();
                    reduced = next;
                }

                return reduced.Width == width && reduced.Height == height ? reduced : ResizeWithKernel(reduced, width, height, filter);
            }
            finally
            {
                if (reduced.Width != width || reduced.Height != height)
                    reduced.Dispose();
            }
        }

        return ResizeWithKernel(source, width, height, filter);
    }

    private static PixelBuffer ResizeWithKernel(PixelBuffer source, int width, int height, ResizeFilter filter)
    {
        var kernel = ResampleKernel.For(filter);
        var horizontal = ResampleWeights.Compute(source.Width, width, kernel);
        var vertical = ResampleWeights.Compute(source.Height, height, kernel);

        var sourceRowFloats = source.Width * 4;
        var destinationRowFloats = width * 4;
        var windowRows = vertical.TapsPerIndex;

        var pool = ArrayPool<float>.Shared;
        var window = pool.Rent(windowRows * sourceRowFloats);
        var accumulator = pool.Rent(sourceRowFloats);
        var resampled = pool.Rent(destinationRowFloats);
        var destination = new PixelBuffer(width, height);
        try
        {
            var loadedUpTo = 0;
            for (var y = 0; y < height; y++)
            {
                var start = vertical.Starts[y];
                var count = vertical.Counts[y];

                // Rows enter the window in order and never come back: convert each one once.
                for (var row = Math.Max(loadedUpTo, start); row < start + count; row++)
                    Widen(source.Row(row), window.AsSpan(row % windowRows * sourceRowFloats, sourceRowFloats));
                loadedUpTo = Math.Max(loadedUpTo, start + count);

                VerticalPass(window, windowRows, sourceRowFloats, vertical, y, accumulator.AsSpan(0, sourceRowFloats));
                HorizontalPass(accumulator.AsSpan(0, sourceRowFloats), horizontal, resampled.AsSpan(0, destinationRowFloats));
                Narrow(resampled.AsSpan(0, destinationRowFloats), destination.Row(y));
            }

            return destination;
        }
        catch
        {
            destination.Dispose();
            throw;
        }
        finally
        {
            pool.Return(window);
            pool.Return(accumulator);
            pool.Return(resampled);
        }
    }

    /// <summary>Destination row <paramref name="y" /> at source width: the weighted sum of the source rows in the window.</summary>
    private static void VerticalPass(float[] window, int windowRows, int rowFloats, ResampleWeights weights, int y, Span<float> output)
    {
        var start = weights.Starts[y];
        var count = weights.Counts[y];
        var w = weights.Weights.AsSpan(y * weights.TapsPerIndex, count);
        var acc = MemoryMarshal.Cast<float, Vector<float>>(output);
        var vectorFloats = acc.Length * Vector<float>.Count;

        var first = window.AsSpan(start % windowRows * rowFloats, rowFloats);
        var firstVectors = MemoryMarshal.Cast<float, Vector<float>>(first);
        var weight0 = new Vector<float>(w[0]);
        for (var v = 0; v < acc.Length; v++)
            acc[v] = firstVectors[v] * weight0;
        for (var i = vectorFloats; i < rowFloats; i++)
            output[i] = first[i] * w[0];

        for (var t = 1; t < count; t++)
        {
            var row = window.AsSpan((start + t) % windowRows * rowFloats, rowFloats);
            var rowVectors = MemoryMarshal.Cast<float, Vector<float>>(row);
            var weight = new Vector<float>(w[t]);
            for (var v = 0; v < acc.Length; v++)
                acc[v] += rowVectors[v] * weight;
            for (var i = vectorFloats; i < rowFloats; i++)
                output[i] += row[i] * w[t];
        }
    }

    /// <summary>One row at source width into one at destination width: for each destination pixel, a weighted sum of source pixels.</summary>
    private static void HorizontalPass(ReadOnlySpan<float> sourceRow, ResampleWeights weights, Span<float> output)
    {
        var source = MemoryMarshal.Cast<float, Vector128<float>>(sourceRow);
        var result = MemoryMarshal.Cast<float, Vector128<float>>(output);
        var taps = weights.TapsPerIndex;
        var allWeights = weights.Weights;
        for (var d = 0; d < result.Length; d++)
        {
            var start = weights.Starts[d];
            var count = weights.Counts[d];
            var w = allWeights.AsSpan(d * taps, count);
            var sum = Vector128<float>.Zero;
            for (var t = 0; t < count; t++)
                sum += source[start + t] * Vector128.Create(w[t]);
            result[d] = sum;
        }
    }

    private static void Widen(ReadOnlySpan<byte> bytes, Span<float> floats)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated && bytes.Length >= Vector<byte>.Count)
        {
            var vectors = MemoryMarshal.Cast<byte, Vector<byte>>(bytes);
            var outVectors = MemoryMarshal.Cast<float, Vector<float>>(floats);
            var o = 0;
            foreach (var v in vectors)
            {
                Vector.Widen(v, out var low16, out var high16);
                Vector.Widen(low16, out var a, out var b);
                Vector.Widen(high16, out var c, out var d);
                outVectors[o++] = Vector.ConvertToSingle(a);
                outVectors[o++] = Vector.ConvertToSingle(b);
                outVectors[o++] = Vector.ConvertToSingle(c);
                outVectors[o++] = Vector.ConvertToSingle(d);
            }

            i = vectors.Length * Vector<byte>.Count;
        }

        for (; i < bytes.Length; i++)
            floats[i] = bytes[i];
    }

    /// <summary>Floats back to bytes, rounded and clamped: a Lanczos sum can overshoot either end.</summary>
    private static void Narrow(ReadOnlySpan<float> floats, Span<byte> bytes)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated && floats.Length >= Vector<float>.Count * 4)
        {
            var vectors = MemoryMarshal.Cast<float, Vector<float>>(floats);
            var outVectors = MemoryMarshal.Cast<byte, Vector<byte>>(bytes);
            var half = new Vector<float>(0.5f);
            var max = new Vector<float>(255f);
            var o = 0;
            for (var v = 0; v + 3 < vectors.Length; v += 4)
            {
                var a = Vector.ConvertToUInt32(Vector.Min(Vector.Max(vectors[v] + half, Vector<float>.Zero), max));
                var b = Vector.ConvertToUInt32(Vector.Min(Vector.Max(vectors[v + 1] + half, Vector<float>.Zero), max));
                var c = Vector.ConvertToUInt32(Vector.Min(Vector.Max(vectors[v + 2] + half, Vector<float>.Zero), max));
                var d = Vector.ConvertToUInt32(Vector.Min(Vector.Max(vectors[v + 3] + half, Vector<float>.Zero), max));
                var ab = Vector.Narrow(a, b);
                var cd = Vector.Narrow(c, d);
                outVectors[o++] = Vector.Narrow(ab, cd);
            }

            i = o * Vector<byte>.Count;
        }

        for (; i < floats.Length; i++)
            bytes[i] = (byte)Math.Clamp(floats[i] + 0.5f, 0f, 255f);
    }

    private static PixelBuffer Nearest(PixelBuffer source, int width, int height)
    {
        var destination = new PixelBuffer(width, height);
        var xScale = (double)source.Width / width;
        var yScale = (double)source.Height / height;
        var columns = new int[width];
        for (var x = 0; x < width; x++)
            columns[x] = Math.Min(source.Width - 1, (int)((x + 0.5) * xScale));

        for (var y = 0; y < height; y++)
        {
            var sourceRow = source.Row(Math.Min(source.Height - 1, (int)((y + 0.5) * yScale)));
            var row = destination.Row(y);
            var sourcePixels = MemoryMarshal.Cast<byte, uint>(sourceRow);
            var pixels = MemoryMarshal.Cast<byte, uint>(row);
            for (var x = 0; x < width; x++)
                pixels[x] = sourcePixels[columns[x]];
        }

        return destination;
    }
}
