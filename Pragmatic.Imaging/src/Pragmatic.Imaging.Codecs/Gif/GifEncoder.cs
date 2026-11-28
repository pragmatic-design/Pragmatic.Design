using System.Buffers;
using Pragmatic.Imaging.Codecs.Quantization;

namespace Pragmatic.Imaging.Codecs.Gif;

/// <summary>
///     Writes RGBA as a single-frame GIF the way the native library did (the image crate 0.25 over the gif crate
///     0.14): alpha is either none or full, and every transparent pixel takes the first transparent one's colour;
///     an image of at most 256 such colours gets exactly those, in ascending order, and so loses nothing; the file
///     is the gif crate's, a two-entry empty global table, a graphic control extension, a local table and its LZW.
///     Where the native library ran NeuQuant over more colours, this takes Wu's palette, with a transparent entry
///     of its own when the image has transparency.
/// </summary>
public static class GifEncoder
{
    private const int MaxSide = ushort.MaxValue;

    [ThreadStatic]
    private static GifLzwEncoder? _lzw;

    /// <exception cref="ArgumentException">The image is wider or taller than a GIF's 65,535 pixels.</exception>
    public static byte[] Encode(PixelBuffer image, bool dither = false)
    {
        using var output = Write(image, dither);
        return output.Written.ToArray();
    }

    public static void Write(PixelBuffer image, Stream output, bool dither = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        using var file = Write(image, dither);
        output.Write(file.Written);
    }

    private static GifOutput Write(PixelBuffer image, bool dither)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var lease = new Rgba8Lease(image);
        image = lease.Image;
        if (image.Width > MaxSide || image.Height > MaxSide)
            throw new ArgumentException($"A GIF is at most {MaxSide} pixels a side; this image is {image.Width} x {image.Height}.", nameof(image));

        var pixels = image.Width * image.Height;
        var rgba = image.Pixels;
        var indices = ArrayPool<byte>.Shared.Rent(pixels);
        Span<byte> palette = stackalloc byte[256 * 3];
        var output = new GifOutput(pixels / 2 + 1024);
        try
        {
            var (entries, transparent) = GifPalette.Build(rgba, image.Width, dither, palette, indices.AsSpan(0, pixels));
            WriteFile(output, image.Width, image.Height, palette[..(entries * 3)], transparent, indices.AsSpan(0, pixels));
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(indices);
        }
    }

    private static void WriteFile(GifOutput output, int width, int height, ReadOnlySpan<byte> palette, int transparent, ReadOnlySpan<byte> indices)
    {
        // The logical screen, with the gif crate's global table of two black entries that no frame uses.
        output.Bytes("GIF89a"u8);
        Span<byte> screen = [(byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), 0x80, 0, 0, 0, 0, 0, 0, 0, 0];
        output.Bytes(screen);

        // The graphic control extension: disposal to background, no delay, and the transparent index if any.
        var flags = (byte)(0x08 | (transparent >= 0 ? 1 : 0));
        Span<byte> control = [0x21, 0xF9, 4, flags, 0, 0, (byte)Math.Max(transparent, 0), 0];
        output.Bytes(control);

        var entries = palette.Length / 3;
        var size = TableSize(entries);
        Span<byte> descriptor = [0x2C, 0, 0, 0, 0, (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), (byte)(0x80 | size)];
        output.Bytes(descriptor);
        output.Bytes(palette);
        for (var k = entries; k < 2 << size; k++)
            output.Bytes([0, 0, 0]);

        var lzw = _lzw ?? new GifLzwEncoder();
        _lzw = null;
        lzw.Encode(indices, MinimumCodeSize(indices), output);
        _lzw = lzw;
        output.Byte(0x3B);
    }

    // The table size field: the smallest power of two of at least two entries that holds them, less one.
    private static int TableSize(int entries)
    {
        var size = 0;
        while (2 << size < Math.Clamp(entries, 2, 255))
            size++;
        return size;
    }

    // The gif crate's minimum code size: from the largest index, scanned until one past 127, and at least 2.
    private static int MinimumCodeSize(ReadOnlySpan<byte> indices)
    {
        var largest = 0;
        foreach (var index in indices)
        {
            if (index <= largest)
                continue;
            largest = index;
            if (index > 127)
                break;
        }

        var size = 2;
        while (1 << size < largest + 1)
            size++;
        return size;
    }
}
