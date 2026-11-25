using Pragmatic.Imaging.Codecs.Transforms;

namespace Pragmatic.Imaging.Codecs.Metadata;

/// <summary>
///     The EXIF Orientation tag (1 to 8) of a JPEG or a PNG, and the pixel transform that shows the image
///     the way the camera meant: a phone stores a portrait photo as a landscape sensor readout with tag 6,
///     and a decoder that ignores the tag shows it lying on its side.
/// </summary>
public static class ExifOrientation
{
    /// <summary>
    ///     The orientation recorded in <paramref name="encoded" />: JPEG APP1 EXIF or PNG eXIf, the first
    ///     one found. 1 (as stored) when there is none, it cannot be read, or the format is neither. The
    ///     bytes may be the start of a file only: what is cut off reads as absent.
    /// </summary>
    public static int Read(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length >= 2 && encoded[0] == 0xFF && encoded[1] == 0xD8)
        {
            foreach (var segment in JpegSegments.Read(encoded, out _, prefixOnly: true))
            {
                var payload = encoded.Slice(segment.DataOffset, segment.DataLength);
                if (segment.Marker == JpegSegments.App1 && payload.StartsWith(ExifTiff.ExifHeader))
                    return ExifTiff.ReadOrientation(payload);
            }

            return 1;
        }

        if (encoded.Length >= PngChunks.SignatureLength && encoded[0] == 0x89 && encoded[1] == (byte)'P')
        {
            foreach (var chunk in PngChunks.Read(encoded, stopAtImageData: true, prefixOnly: true))
            {
                if (chunk.Type == PngChunks.Exif)
                    return ExifTiff.ReadOrientation(encoded.Slice(chunk.DataOffset, chunk.DataLength));
            }
        }

        return 1;
    }

    /// <summary>Whether showing <paramref name="orientation" /> swaps width and height (5 to 8).</summary>
    public static bool SwapsAxes(int orientation) => orientation is >= 5 and <= 8;

    /// <summary>
    ///     The image as shown under <paramref name="orientation" />. For 1 (or an unknown value) it is
    ///     <paramref name="image" /> itself; for a mirror (2, 4) it is <paramref name="image" />, changed
    ///     in place; otherwise a new buffer, and <paramref name="image" /> is disposed.
    /// </summary>
    public static PixelBuffer Apply(PixelBuffer image, int orientation)
    {
        ArgumentNullException.ThrowIfNull(image);
        switch (orientation)
        {
            case 2:
                PixelGeometry.FlipHorizontal(image);
                return image;
            case 4:
                PixelGeometry.FlipVertical(image);
                return image;
            case 3:
                return Replace(image, PixelGeometry.Rotate180);
            case 5:
                return Replace(image, PixelGeometry.Transpose);
            case 6:
                return Replace(image, PixelGeometry.Rotate90);
            case 7:
                return Replace(image, PixelGeometry.Transverse);
            case 8:
                return Replace(image, PixelGeometry.Rotate270);
            default:
                return image;
        }
    }

    private static PixelBuffer Replace(PixelBuffer image, Func<PixelBuffer, PixelBuffer> transform)
    {
        using (image)
            return transform(image);
    }
}
