using System.Buffers.Binary;
using Pragmatic.Imaging.Codecs.Png;

namespace Pragmatic.Imaging.Codecs.Metadata;

/// <summary>
///     Removes EXIF and XMP metadata from a JPEG or a PNG by copying the file without those segments: the
///     pixels are never decoded, so a JPEG keeps its exact compressed data and loses no quality. What goes
///     is every JPEG APP1 segment (EXIF, XMP) and every PNG eXIf chunk. What stays is everything else:
///     JFIF, colour profiles, comments, IPTC in APP13, PNG text chunks.
/// </summary>
/// <remarks>
///     The Orientation tag is kept, alone. Applying it to the pixels instead would mean re-encoding the
///     JPEG, the loss this exists to avoid; dropping it would show a phone photo on its side. So when the
///     source has one other than 1, the output carries a new EXIF block with that tag and nothing else:
///     no camera, no GPS, no timestamps.
/// </remarks>
public static class MetadataStripper
{
    /// <summary>The JPEG without its APP1 segments, plus an orientation-only APP1 when one is needed.</summary>
    public static byte[] StripJpeg(ReadOnlySpan<byte> jpeg)
    {
        var segments = JpegSegments.Read(jpeg, out var scanStart);
        var orientation = ExifOrientation.Read(jpeg);
        var exif = orientation == 1 ? [] : OrientationSegment(orientation);

        var removed = 0;
        foreach (var s in segments)
            if (s.Marker == JpegSegments.App1)
                removed += s.Length;

        var output = new byte[jpeg.Length - removed + exif.Length];
        var at = 0;
        jpeg[..2].CopyTo(output); // SOI
        at += 2;

        // After a JFIF APP0, which readers expect first; else right after SOI.
        var next = 2;
        if (segments.Count > 0 && segments[0].Marker == 0xE0)
        {
            jpeg.Slice(segments[0].Offset, segments[0].Length).CopyTo(output.AsSpan(at));
            at += segments[0].Length;
            next = segments[0].Offset + segments[0].Length;
        }

        exif.CopyTo(output.AsSpan(at));
        at += exif.Length;

        foreach (var s in segments)
        {
            if (s.Offset < next)
                continue;
            // Bytes between segments (fill bytes) are copied along with what follows them.
            if (s.Marker == JpegSegments.App1)
            {
                jpeg[next..s.Offset].CopyTo(output.AsSpan(at));
                at += s.Offset - next;
                next = s.Offset + s.Length;
            }
        }

        jpeg[next..].CopyTo(output.AsSpan(at));
        return output;
    }

    /// <summary>The PNG without its eXIf chunks, plus an orientation-only eXIf when one is needed.</summary>
    public static byte[] StripPng(ReadOnlySpan<byte> png)
    {
        var chunks = PngChunks.Read(png);
        if (chunks.Count == 0 || chunks[0].Type != PngChunk.Ihdr)
            throw new ImageDecodeException("PNG does not start with IHDR.");
        var orientation = ExifOrientation.Read(png);

        using var output = new MemoryStream(png.Length);
        output.Write(png[..PngChunks.SignatureLength]);
        foreach (var chunk in chunks)
        {
            if (chunk.Type == PngChunks.Exif)
                continue;
            output.Write(png.Slice(chunk.DataOffset - 8, chunk.DataLength + 12));
            if (chunk.Type == PngChunk.Ihdr && orientation != 1)
                PngChunkWriter.Write(output, PngChunks.Exif, ExifTiff.OrientationOnly(orientation));
        }

        return output.ToArray();
    }

    private static byte[] OrientationSegment(int orientation)
    {
        var tiff = ExifTiff.OrientationOnly(orientation);
        var header = ExifTiff.ExifHeader;
        var payload = header.Length + tiff.Length;
        var segment = new byte[4 + payload];
        segment[0] = 0xFF;
        segment[1] = JpegSegments.App1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload + 2));
        header.CopyTo(segment.AsSpan(4));
        tiff.CopyTo(segment.AsSpan(4 + header.Length));
        return segment;
    }
}
