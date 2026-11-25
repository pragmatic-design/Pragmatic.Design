using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.Transforms;

/// <summary>
///     Crops, quarter turns, mirrors and the two diagonal flips EXIF orientation needs. Each moves whole
///     pixels as 32-bit words and computes nothing, so the result is exactly what the native library's
///     <c>image</c> crate produces for the same call. The flips work in place; every operation that
///     changes the shape returns a new buffer, which the caller owns.
/// </summary>
public static class PixelGeometry
{
    /// <summary>Copies the region <paramref name="x" />, <paramref name="y" />, <paramref name="width" /> × <paramref name="height" />.</summary>
    public static PixelBuffer Crop(PixelBuffer source, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if ((long)x + width > source.Width || (long)y + height > source.Height)
            throw new ArgumentOutOfRangeException(nameof(width), $"The region ({x},{y},{width},{height}) exceeds the image ({source.Width}x{source.Height}).");

        var result = new PixelBuffer(width, height);
        for (var row = 0; row < height; row++)
            source.Row(y + row).Slice(x * 4, width * 4).CopyTo(result.Row(row));
        return result;
    }

    /// <summary>A quarter turn clockwise: the left column becomes the top row.</summary>
    public static PixelBuffer Rotate90(PixelBuffer source) => Remap(source, static (y, w, h) => ((h - 1) * w + y, -w));

    /// <summary>A half turn.</summary>
    public static PixelBuffer Rotate180(PixelBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new PixelBuffer(source.Width, source.Height);
        var from = Words(source);
        var to = Words(result);
        var last = from.Length - 1;
        for (var i = 0; i < from.Length; i++)
            to[i] = from[last - i];
        return result;
    }

    /// <summary>A quarter turn counter-clockwise: the right column becomes the top row.</summary>
    public static PixelBuffer Rotate270(PixelBuffer source) => Remap(source, static (y, w, h) => (w - 1 - y, w));

    /// <summary>Mirrors left to right, in place.</summary>
    public static void FlipHorizontal(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        for (var y = 0; y < image.Height; y++)
            MemoryMarshal.Cast<byte, uint>(image.Row(y)).Reverse();
    }

    /// <summary>Mirrors top to bottom, in place.</summary>
    public static void FlipVertical(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var stride = image.Stride;
        var scratch = new byte[stride];
        for (int top = 0, bottom = image.Height - 1; top < bottom; top++, bottom--)
        {
            var a = image.Row(top);
            var b = image.Row(bottom);
            a.CopyTo(scratch);
            b.CopyTo(a);
            scratch.CopyTo(b);
        }
    }

    /// <summary>Mirrors across the main diagonal: pixel (x, y) moves to (y, x). EXIF orientation 5.</summary>
    public static PixelBuffer Transpose(PixelBuffer source) => Remap(source, static (y, w, h) => (y, w));

    /// <summary>Mirrors across the anti-diagonal. EXIF orientation 7.</summary>
    public static PixelBuffer Transverse(PixelBuffer source) => Remap(source, static (y, w, h) => ((h - 1) * w + (w - 1 - y), -w));

    /// <summary>
    ///     The four operations that swap the axes. Destination row <c>y</c> is one source column read
    ///     with a fixed step, so <paramref name="column" /> answers, for that row and the source's size,
    ///     the index of its first source pixel and the step to the next. Destination rows are written
    ///     in order: the writes stream and the reads stride, the cheaper of the two to leave uncached.
    /// </summary>
    private static PixelBuffer Remap(PixelBuffer source, Func<int, int, int, (int Start, int Step)> column)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (w, h) = (source.Width, source.Height);
        var result = new PixelBuffer(h, w);
        var from = Words(source);
        for (var y = 0; y < result.Height; y++)
        {
            var row = MemoryMarshal.Cast<byte, uint>(result.Row(y));
            var (index, step) = column(y, w, h);
            for (var x = 0; x < row.Length; x++, index += step)
                row[x] = from[index];
        }

        return result;
    }

    private static Span<uint> Words(PixelBuffer image) => MemoryMarshal.Cast<byte, uint>(image.Pixels);
}
