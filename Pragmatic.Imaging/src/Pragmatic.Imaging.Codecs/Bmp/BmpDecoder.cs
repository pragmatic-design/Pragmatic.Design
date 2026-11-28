using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.Bmp;

/// <summary>
///     Decodes BMP to RGBA the way the native library read it (the image crate 0.25): palette images of 1, 2,
///     4 and 8 bits, RLE8 and RLE4, 16-bit (5-5-5 or bitfields), 24-bit, and 32-bit (no alpha unless a bitfield
///     mask gives it one); bottom-up and top-down; the core, info, V2, V3, V4 and V5 headers. An image without
///     alpha is opaque.
/// </summary>
public static class BmpDecoder
{
    /// <summary>Whether the bytes start like a BMP file.</summary>
    public static bool IsBmp(ReadOnlySpan<byte> encoded) => BmpHeader.HasSignature(encoded);

    /// <summary>The size of the image, from its headers.</summary>
    /// <exception cref="ImageDecodeException">The bytes are not a BMP this decoder reads.</exception>
    public static (int Width, int Height) Identify(ReadOnlySpan<byte> encoded)
    {
        var header = BmpHeader.Read(encoded);
        return (header.Width, header.Height);
    }

    /// <exception cref="ImageDecodeException">The bytes are truncated, corrupt, or not a BMP this decoder reads.</exception>
    public static PixelBuffer Decode(ReadOnlySpan<byte> encoded)
    {
        var header = BmpHeader.Read(encoded);
        var image = new PixelBuffer(header.Width, header.Height);
        try
        {
            var rgba = image.Pixels;
            Span<byte> palette = stackalloc byte[BmpHeader.PaletteBytes];
            switch (header.Layout)
            {
                case BmpLayout.Rle8 or BmpLayout.Rle4:
                    // What the runs do not reach is black; the image has no alpha, so it is opaque.
                    MemoryMarshal.Cast<byte, uint>(rgba).Fill(BitConverter.IsLittleEndian ? 0xFF000000u : 0x000000FFu);
                    header.ReadPalette(encoded, palette);
                    BmpRle.Decode(encoded, header, palette, rgba);
                    break;
                case BmpLayout.Palette:
                    header.ReadPalette(encoded, palette);
                    Rows(encoded, header, rgba, palette);
                    break;
                default:
                    Rows(encoded, header, rgba, palette);
                    break;
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>The uncompressed layouts, a row at a time, each row padded to four bytes.</summary>
    private static void Rows(ReadOnlySpan<byte> data, in BmpHeader header, Span<byte> rgba, ReadOnlySpan<byte> palette)
    {
        var width = header.Width;
        var height = header.Height;
        var stride = (int)(((long)header.BitCount * width + 31) / 32 * 4);
        if (header.DataOffset > data.Length || (long)stride * height > data.Length - header.DataOffset)
            throw new ImageDecodeException("BMP: the pixel data is truncated.");

        for (var stored = 0; stored < height; stored++)
        {
            var source = data.Slice(header.DataOffset + stored * stride, stride);
            var y = header.TopDown ? stored : height - 1 - stored;
            var target = rgba.Slice(y * width * 4, width * 4);
            switch (header.Layout)
            {
                case BmpLayout.Palette:
                    Indexed(source, target, width, header.BitCount, palette);
                    break;
                case BmpLayout.Rgb24:
                    for (var x = 0; x < width; x++)
                    {
                        target[x * 4] = source[x * 3 + 2];
                        target[x * 4 + 1] = source[x * 3 + 1];
                        target[x * 4 + 2] = source[x * 3];
                        target[x * 4 + 3] = 0xFF;
                    }

                    break;
                case BmpLayout.Rgb32:
                    for (var x = 0; x < width; x++)
                    {
                        target[x * 4] = source[x * 4 + 2];
                        target[x * 4 + 1] = source[x * 4 + 1];
                        target[x * 4 + 2] = source[x * 4];
                        target[x * 4 + 3] = 0xFF;
                    }

                    break;
                default:
                    Bitfields(source, target, width, header);
                    break;
            }
        }
    }

    private static void Indexed(ReadOnlySpan<byte> source, Span<byte> target, int width, int bits, ReadOnlySpan<byte> palette)
    {
        var perByte = 8 / bits;
        var mask = (1 << bits) - 1;
        for (var x = 0; x < width; x++)
        {
            var shift = 8 - bits * (x % perByte + 1);
            var index = (source[x / perByte] >> shift) & mask;
            target[x * 4] = palette[index * 3];
            target[x * 4 + 1] = palette[index * 3 + 1];
            target[x * 4 + 2] = palette[index * 3 + 2];
            target[x * 4 + 3] = 0xFF;
        }
    }

    private static void Bitfields(ReadOnlySpan<byte> source, Span<byte> target, int width, in BmpHeader header)
    {
        var wide = header.BitCount == 32;
        for (var x = 0; x < width; x++)
        {
            uint pixel = wide ? BinaryPrimitives.ReadUInt32LittleEndian(source[(x * 4)..]) : BinaryPrimitives.ReadUInt16LittleEndian(source[(x * 2)..]);
            target[x * 4] = header.Red.Read(pixel);
            target[x * 4 + 1] = header.Green.Read(pixel);
            target[x * 4 + 2] = header.Blue.Read(pixel);
            target[x * 4 + 3] = header.Alpha.IsPresent ? header.Alpha.Read(pixel) : (byte)0xFF;
        }
    }
}
