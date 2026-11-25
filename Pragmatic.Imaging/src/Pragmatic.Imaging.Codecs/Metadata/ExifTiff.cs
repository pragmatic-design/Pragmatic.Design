using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.Metadata;

/// <summary>
///     The TIFF structure EXIF data is stored in (JPEG APP1 after its <c>Exif\0\0</c> header, PNG eXIf
///     as is), read for one tag, written with one tag. Every offset is checked against the data: a
///     malformed block reads as "no orientation", never as an exception or a read past the end.
/// </summary>
internal static class ExifTiff
{
    private const ushort OrientationTag = 0x0112;
    private const ushort ShortType = 3;

    /// <summary>The prefix of EXIF data inside a JPEG APP1 segment.</summary>
    public static ReadOnlySpan<byte> ExifHeader => "Exif\0\0"u8;

    /// <summary>The Orientation tag of IFD0, 1 to 8, or 1 when the block has none or cannot be read.</summary>
    public static int ReadOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.StartsWith(ExifHeader))
            tiff = tiff[ExifHeader.Length..];
        if (tiff.Length < 8)
            return 1;

        bool bigEndian;
        if (tiff[0] == 'M' && tiff[1] == 'M')
            bigEndian = true;
        else if (tiff[0] == 'I' && tiff[1] == 'I')
            bigEndian = false;
        else
            return 1;

        if (Read16(tiff[2..], bigEndian) != 42)
            return 1;
        var ifd = Read32(tiff[4..], bigEndian);
        if (ifd > (uint)tiff.Length - 2)
            return 1;

        var count = Read16(tiff[(int)ifd..], bigEndian);
        var entries = (int)ifd + 2;
        for (var i = 0; i < count; i++)
        {
            var at = entries + i * 12;
            if (at > tiff.Length - 12)
                return 1;
            var entry = tiff.Slice(at, 12);
            if (Read16(entry, bigEndian) != OrientationTag)
                continue;
            if (Read16(entry[2..], bigEndian) != ShortType || Read32(entry[4..], bigEndian) != 1)
                return 1;
            var value = Read16(entry[8..], bigEndian);
            return value is >= 1 and <= 8 ? value : 1;
        }

        return 1;
    }

    /// <summary>A big-endian TIFF block whose IFD0 holds the Orientation tag and nothing else.</summary>
    public static byte[] OrientationOnly(int orientation)
    {
        if (orientation is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "EXIF orientation is 1 to 8.");

        var tiff = new byte[26];
        tiff[0] = (byte)'M';
        tiff[1] = (byte)'M';
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(4), 8); // IFD0 right after the header
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(8), 1); // one entry
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(10), OrientationTag);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(12), ShortType);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(14), 1); // count
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(18), (ushort)orientation); // value, left-justified
        // Bytes 20..21 pad the value field; 22..25 are the next-IFD offset, zero: there is none.
        return tiff;
    }

    private static ushort Read16(ReadOnlySpan<byte> data, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data) : BinaryPrimitives.ReadUInt16LittleEndian(data);

    private static uint Read32(ReadOnlySpan<byte> data, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(data) : BinaryPrimitives.ReadUInt32LittleEndian(data);
}
