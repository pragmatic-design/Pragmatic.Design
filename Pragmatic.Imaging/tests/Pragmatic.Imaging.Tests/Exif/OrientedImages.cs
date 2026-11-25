using System.Buffers.Binary;
using System.Text;
using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.Jpeg;
using Pragmatic.Imaging.Codecs.Png;

namespace Pragmatic.Imaging.Tests.Exif;

/// <summary>
///     Fixtures with an EXIF orientation, built here so their provenance is this file: a 32×16 image, red
///     on the left half and blue on the right, encoded by the managed core, with an EXIF block that holds
///     a camera make (metadata to strip) and the orientation. SkiaSharp, an independent decoder, confirms
///     the JPEG's block is well formed (see <see cref="ExifFixtureTests" />).
/// </summary>
internal static class OrientedImages
{
    public const int Width = 32;
    public const int Height = 16;
    public const string CameraMake = "PragmaticTestCamera";

    /// <summary>A JPEG whose APP1 EXIF, right after SOI, records <paramref name="orientation" />.</summary>
    public static byte[] Jpeg(int orientation)
    {
        using var image = Pixels();
        using var encoded = new MemoryStream();
        JpegEncoder.Write(image, encoded, quality: 95);
        var plain = encoded.ToArray();

        var tiff = Tiff(orientation);
        var app1 = new byte[4 + 6 + tiff.Length];
        app1[0] = 0xFF;
        app1[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(app1.AsSpan(2), (ushort)(2 + 6 + tiff.Length));
        "Exif\0\0"u8.CopyTo(app1.AsSpan(4));
        tiff.CopyTo(app1.AsSpan(10));

        return [.. plain.AsSpan(0, 2), .. app1, .. plain.AsSpan(2)];
    }

    /// <summary>A PNG whose eXIf chunk, right after IHDR, records <paramref name="orientation" />.</summary>
    public static byte[] Png(int orientation)
    {
        using var image = Pixels();
        using var encoded = new MemoryStream();
        PngEncoder.Write(image, encoded);
        var plain = encoded.ToArray();

        // Signature (8) + IHDR chunk (4 length + 4 type + 13 data + 4 CRC).
        const int afterIhdr = 8 + 25;
        var chunk = Chunk("eXIf"u8, Tiff(orientation));
        return [.. plain.AsSpan(0, afterIhdr), .. chunk, .. plain.AsSpan(afterIhdr)];
    }

    /// <summary>The source pixels: red where x &lt; 16, blue elsewhere, opaque.</summary>
    public static PixelBuffer Pixels()
    {
        var image = new PixelBuffer(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            var row = image.Row(y);
            for (var x = 0; x < Width; x++)
            {
                row[x * 4] = (byte)(x < Width / 2 ? 255 : 0);
                row[x * 4 + 1] = 0;
                row[x * 4 + 2] = (byte)(x < Width / 2 ? 0 : 255);
                row[x * 4 + 3] = 255;
            }
        }

        return image;
    }

    /// <summary>The offset of the JPEG's first SOS marker: what follows it is the entropy-coded data.</summary>
    public static int ScanStart(byte[] jpeg)
    {
        for (var i = 2; i < jpeg.Length - 1; i++)
        {
            if (jpeg[i] == 0xFF && jpeg[i + 1] == 0xDA)
                return i;
            if (jpeg[i] == 0xFF && jpeg[i + 1] is not (0x00 or 0xFF or 0xD8))
                i += 1 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(i + 2));
        }

        throw new InvalidOperationException("The JPEG has no SOS marker.");
    }

    public static bool Contains(byte[] data, string text) => data.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;

    /// <summary>Big-endian TIFF, IFD0 with Make (ASCII) and Orientation (SHORT), in tag order.</summary>
    private static byte[] Tiff(int orientation)
    {
        var make = Encoding.ASCII.GetBytes(CameraMake + "\0");
        const int ifd = 8;
        const int entries = 2;
        var makeOffset = ifd + 2 + entries * 12 + 4;
        var tiff = new byte[makeOffset + make.Length];
        "MM"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(4), ifd);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(ifd), entries);

        var at = ifd + 2;
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at), 0x010F); // Make
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at + 2), 2); // ASCII
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(at + 4), (uint)make.Length);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(at + 8), (uint)makeOffset);

        at += 12;
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at), 0x0112); // Orientation
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at + 2), 3); // SHORT
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(at + 4), 1);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at + 8), (ushort)orientation);

        make.CopyTo(tiff.AsSpan(makeOffset));
        return tiff;
    }

    private static byte[] Chunk(ReadOnlySpan<byte> type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        type.CopyTo(chunk.AsSpan(4));
        data.CopyTo(chunk.AsSpan(8));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc(chunk.AsSpan(4, 4 + data.Length)));
        return chunk;
    }

    private static uint Crc(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        return ~crc;
    }
}
