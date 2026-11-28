using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.Transforms;

/// <summary>
///     Crops, quarter turns, mirrors and the two diagonal flips EXIF orientation needs, on any pixel format. Each moves
///     whole pixels and computes nothing, so an 8-bit RGBA result is exactly what the native library's <c>image</c>
///     crate produces for the same call, and no format loses anything. The flips work in place; every operation that
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

        var bytes = source.BytesPerPixel;
        var result = new PixelBuffer(width, height, source.Format);
        for (var row = 0; row < height; row++)
            source.Row(y + row).Slice(x * bytes, width * bytes).CopyTo(result.Row(row));
        return result;
    }

    /// <summary>A quarter turn clockwise: the left column becomes the top row.</summary>
    public static PixelBuffer Rotate90(PixelBuffer source) => Remap(source, static (y, w, h) => ((h - 1) * w + y, -w));

    /// <summary>A half turn.</summary>
    public static PixelBuffer Rotate180(PixelBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new PixelBuffer(source.Width, source.Height, source.Format);
        switch (source.BytesPerPixel)
        {
            case 2:
                ReverseInto<ushort>(source, result);
                break;
            case 4:
                ReverseInto<uint>(source, result);
                break;
            case 8:
                ReverseInto<ulong>(source, result);
                break;
            case 16:
                ReverseInto<UInt128>(source, result);
                break;
            default:
                source.Pixels.CopyTo(result.Pixels);
                Reverse(result.Pixels, source.BytesPerPixel);
                break;
        }

        return result;
    }

    // One pass: the last source pixel to the first destination one, and on.
    private static void ReverseInto<T>(PixelBuffer source, PixelBuffer result)
        where T : unmanaged
    {
        var from = MemoryMarshal.Cast<byte, T>(source.Pixels);
        var to = MemoryMarshal.Cast<byte, T>(result.Pixels);
        var last = from.Length - 1;
        for (var i = 0; i < from.Length; i++)
            to[i] = from[last - i];
    }

    /// <summary>A quarter turn counter-clockwise: the right column becomes the top row.</summary>
    public static PixelBuffer Rotate270(PixelBuffer source) => Remap(source, static (y, w, h) => (w - 1 - y, w));

    /// <summary>Mirrors left to right, in place.</summary>
    public static void FlipHorizontal(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        for (var y = 0; y < image.Height; y++)
            Reverse(image.Row(y), image.BytesPerPixel);
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

    // The pixels of a span in reverse order, whole pixels at a time.
    private static void Reverse(Span<byte> pixels, int bytes)
    {
        switch (bytes)
        {
            case 1:
                pixels.Reverse();
                return;
            case 2:
                MemoryMarshal.Cast<byte, ushort>(pixels).Reverse();
                return;
            case 4:
                MemoryMarshal.Cast<byte, uint>(pixels).Reverse();
                return;
            case 8:
                MemoryMarshal.Cast<byte, ulong>(pixels).Reverse();
                return;
            case 16:
                MemoryMarshal.Cast<byte, UInt128>(pixels).Reverse();
                return;
            default:
                Span<byte> swap = stackalloc byte[bytes];
                for (int left = 0, right = pixels.Length - bytes; left < right; left += bytes, right -= bytes)
                {
                    pixels.Slice(left, bytes).CopyTo(swap);
                    pixels.Slice(right, bytes).CopyTo(pixels.Slice(left, bytes));
                    swap.CopyTo(pixels.Slice(right, bytes));
                }

                return;
        }
    }

    /// <summary>
    ///     The four operations that swap the axes. Destination row <c>y</c> is one source column read
    ///     with a fixed step, so <paramref name="column" /> answers, for that row and the source's size,
    ///     the index of its first source pixel and the step to the next. Destination rows are written
    ///     in order: the writes stream and the reads stride, the cheaper of the two to leave uncached.
    /// </summary>
    private static PixelBuffer Remap(PixelBuffer source, Func<int, int, int, (int Start, int Step)> column)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new PixelBuffer(source.Height, source.Width, source.Format);
        switch (source.BytesPerPixel)
        {
            case 1:
                Remap<byte>(source, result, column);
                break;
            case 2:
                Remap<ushort>(source, result, column);
                break;
            case 4:
                Remap<uint>(source, result, column);
                break;
            case 8:
                Remap<ulong>(source, result, column);
                break;
            case 16:
                Remap<UInt128>(source, result, column);
                break;
            default:
                RemapBytes(source, result, column);
                break;
        }

        return result;
    }

    private static void Remap<T>(PixelBuffer source, PixelBuffer result, Func<int, int, int, (int Start, int Step)> column)
        where T : unmanaged
    {
        var (w, h) = (source.Width, source.Height);
        var from = MemoryMarshal.Cast<byte, T>(source.Pixels);
        for (var y = 0; y < result.Height; y++)
        {
            var row = MemoryMarshal.Cast<byte, T>(result.Row(y));
            var (index, step) = column(y, w, h);
            for (var x = 0; x < row.Length; x++, index += step)
                row[x] = from[index];
        }
    }

    // Three and six-byte pixels, which no machine word holds: copied a pixel at a time.
    private static void RemapBytes(PixelBuffer source, PixelBuffer result, Func<int, int, int, (int Start, int Step)> column)
    {
        var (w, h) = (source.Width, source.Height);
        var bytes = source.BytesPerPixel;
        var from = source.Pixels;
        for (var y = 0; y < result.Height; y++)
        {
            var row = result.Row(y);
            var (index, step) = column(y, w, h);
            for (var x = 0; x < result.Width; x++, index += step)
                from.Slice(index * bytes, bytes).CopyTo(row[(x * bytes)..]);
        }
    }
}
