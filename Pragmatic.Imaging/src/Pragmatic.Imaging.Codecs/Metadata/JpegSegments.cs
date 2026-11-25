using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.Metadata;

/// <summary>
///     The marker segments of a JPEG before its first scan, as offsets into the file. From the start of
///     scan on, everything is entropy-coded data and is never parsed here: metadata lives before it.
/// </summary>
internal static class JpegSegments
{
    public const byte App1 = 0xE1;
    private const byte Soi = 0xD8;
    private const byte Sos = 0xDA;
    private const byte Eoi = 0xD9;

    /// <summary>
    ///     The segments between SOI and SOS, and where the SOS marker starts (the file's length when it
    ///     has none). Throws <see cref="ImageDecodeException" /> when the bytes are not a JPEG or a
    ///     segment runs past the end, unless <paramref name="prefixOnly" /> says the bytes are the start
    ///     of a file: then the segments read before the cut are returned.
    /// </summary>
    public static List<JpegSegment> Read(ReadOnlySpan<byte> data, out int scanStart, bool prefixOnly = false)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != Soi)
            throw new ImageDecodeException("The bytes do not start with a JPEG SOI marker.");

        var segments = new List<JpegSegment>();
        var position = 2;
        while (position < data.Length)
        {
            if (data[position] != 0xFF)
                throw new ImageDecodeException($"JPEG has no marker at offset {position}, where a segment should begin.");

            var markerAt = position;
            while (position < data.Length && data[position] == 0xFF)
                position++; // fill bytes before the marker
            if (position >= data.Length)
                break;

            var marker = data[position++];
            if (marker is Sos or Eoi)
            {
                scanStart = markerAt;
                return segments;
            }

            if (marker is 0x01 or >= 0xD0 and <= 0xD7)
                continue; // TEM and RSTn stand alone

            var length = data.Length - position < 2 ? 0 : BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
            if (length < 2 || data.Length - position < length)
            {
                if (prefixOnly)
                    break;
                throw new ImageDecodeException("JPEG segment runs past the end of the data.");
            }

            segments.Add(new JpegSegment(marker, markerAt, position + length - markerAt, position + 2, length - 2));
            position += length;
        }

        scanStart = data.Length;
        return segments;
    }
}
