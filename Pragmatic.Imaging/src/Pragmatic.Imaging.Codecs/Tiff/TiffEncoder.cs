using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     Writes RGBA as the file the native library wrote (the image crate 0.25 over the TIFF crate 0.11): little-endian,
///     uncompressed, four 8-bit samples a pixel with no extra-samples tag, strips of about a megabyte, a resolution of
///     one to one with no unit. The values that do not fit in their entries come where the crate wrote them: the two
///     resolutions before the strips, the depths, sample formats and strip lists after them, then the directory.
/// </summary>
public static class TiffEncoder
{
    private const ushort Short = 3;
    private const ushort Long = 4;
    private const ushort Rational = 5;
    private const int Entries = 14;
    private const int DirectoryBytes = 2 + Entries * 12 + 4;

    /// <exception cref="ArgumentException">The file would pass 4 GB, beyond a TIFF's offsets.</exception>
    public static byte[] Encode(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var (strips, rowsPerStrip, tailBytes) = Plan(image);
        var file = new byte[24 + image.ByteLength + tailBytes + DirectoryBytes];
        WriteHead(file, image, tailBytes);
        image.Pixels.CopyTo(file.AsSpan(24));
        WriteTail(file.AsSpan(24 + image.ByteLength), image, strips, rowsPerStrip);
        return file;
    }

    /// <exception cref="ArgumentException">The file would pass 4 GB, beyond a TIFF's offsets.</exception>
    public static void Write(PixelBuffer image, Stream output)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(output);
        var (strips, rowsPerStrip, tailBytes) = Plan(image);
        Span<byte> head = stackalloc byte[24];
        WriteHead(head, image, tailBytes);
        output.Write(head);
        output.Write(image.Pixels);
        var tail = new byte[tailBytes + DirectoryBytes];
        WriteTail(tail, image, strips, rowsPerStrip);
        output.Write(tail);
    }

    // The strips of about a megabyte, and the bytes the values after them take: the depths and sample formats, and
    // the strip lists when there is more than one strip.
    private static (int Strips, long RowsPerStrip, int TailBytes) Plan(PixelBuffer image)
    {
        var rowBytes = (long)image.Width * 4;
        var rowsPerStrip = (1_000_000 + rowBytes - 1) / rowBytes;
        var strips = (int)((image.Height + rowsPerStrip - 1) / rowsPerStrip);
        var tail = 16 + (strips > 1 ? strips * 8 : 0);
        if (24L + image.ByteLength + tail + DirectoryBytes > uint.MaxValue)
            throw new ArgumentException("A TIFF of this image would pass 4 GB, beyond its offsets.", nameof(image));
        return (strips, rowsPerStrip, tail);
    }

    // The header, with the directory's offset, and the two rationals of 1/1 at 8 and 16.
    private static void WriteHead(Span<byte> head, PixelBuffer image, int tailBytes)
    {
        "II*\0"u8.CopyTo(head);
        BinaryPrimitives.WriteUInt32LittleEndian(head[4..], (uint)(24 + image.ByteLength + tailBytes));
        for (var i = 8; i < 24; i += 4)
            BinaryPrimitives.WriteUInt32LittleEndian(head[i..], 1);
    }

    // The values after the strips, then the directory, its entries in tag order.
    private static void WriteTail(Span<byte> tail, PixelBuffer image, int strips, long rowsPerStrip)
    {
        var dataEnd = (uint)(24 + image.ByteLength);
        var rowBytes = (long)image.Width * 4;
        for (var i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(tail[(i * 2)..], 8);
            BinaryPrimitives.WriteUInt16LittleEndian(tail[(8 + i * 2)..], 1);
        }

        uint offsetsAt = 0, countsAt = 0;
        if (strips > 1)
        {
            offsetsAt = dataEnd + 16;
            countsAt = offsetsAt + (uint)strips * 4;
            for (var s = 0; s < strips; s++)
            {
                var first = s * rowsPerStrip;
                BinaryPrimitives.WriteUInt32LittleEndian(tail[(16 + s * 4)..], (uint)(24 + first * rowBytes));
                BinaryPrimitives.WriteUInt32LittleEndian(tail[(16 + strips * 4 + s * 4)..],
                    (uint)(Math.Min(rowsPerStrip, image.Height - first) * rowBytes));
            }
        }

        var directory = tail[(16 + (strips > 1 ? strips * 8 : 0))..];
        BinaryPrimitives.WriteUInt16LittleEndian(directory, Entries);
        var at = 2;
        void Entry(ushort tag, ushort type, uint count, uint value, Span<byte> into)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(into[at..], tag);
            BinaryPrimitives.WriteUInt16LittleEndian(into[(at + 2)..], type);
            BinaryPrimitives.WriteUInt32LittleEndian(into[(at + 4)..], count);
            if (type == Short && count == 1)
                BinaryPrimitives.WriteUInt16LittleEndian(into[(at + 8)..], (ushort)value);
            else
                BinaryPrimitives.WriteUInt32LittleEndian(into[(at + 8)..], value);
            at += 12;
        }

        Entry(256, Long, 1, (uint)image.Width, directory);
        Entry(257, Long, 1, (uint)image.Height, directory);
        Entry(258, Short, 4, dataEnd, directory);
        Entry(259, Short, 1, 1, directory);
        Entry(262, Short, 1, 2, directory);
        Entry(273, Long, (uint)strips, strips > 1 ? offsetsAt : 24, directory);
        Entry(277, Short, 1, 4, directory);
        Entry(278, Long, 1, (uint)rowsPerStrip, directory);
        Entry(279, Long, (uint)strips, strips > 1 ? countsAt : (uint)(image.Height * rowBytes), directory);
        Entry(282, Rational, 1, 8, directory);
        Entry(283, Rational, 1, 16, directory);
        Entry(296, Short, 1, 1, directory);
        Entry(317, Short, 1, 1, directory);
        Entry(339, Short, 4, dataEnd + 8, directory);
        BinaryPrimitives.WriteUInt32LittleEndian(directory[at..], 0);
    }
}
