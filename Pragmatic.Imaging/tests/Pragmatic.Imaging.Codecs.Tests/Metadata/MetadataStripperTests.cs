using Pragmatic.Imaging.Codecs.Jpeg;
using Pragmatic.Imaging.Codecs.Metadata;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests.Metadata;

/// <summary>
///     Stripping a JPEG removes every APP1 (EXIF and XMP), keeps every other byte in order, writes back an
///     orientation-only EXIF block after the JFIF segment when the source had an orientation, and leaves
///     a file that decodes to the same pixels.
/// </summary>
public class MetadataStripperTests
{
    private static readonly byte[] Xmp = [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. "<x:xmpmeta>secret</x:xmpmeta>"u8];

    [Fact]
    public void Every_app1_goes_and_every_other_byte_stays_in_order()
    {
        var plain = Jpegs.Plain();
        var app0 = Jpegs.SegmentLength(plain, 2);
        // SOI, JFIF APP0, EXIF APP1 (orientation 1), XMP APP1, then the rest of the encoder's file.
        var withExif = Jpegs.Insert(plain, 2 + app0, Jpegs.Segment(0xE1, [.. "Exif\0\0"u8, .. ExifOrientationTests.Tiff(1, bigEndian: true)]));
        var source = Jpegs.Insert(withExif, 2 + app0 + Jpegs.SegmentLength(withExif, 2 + app0), Jpegs.Segment(0xE1, Xmp));

        var stripped = MetadataStripper.StripJpeg(source);

        Assert.Equal(plain, stripped);
    }

    [Fact]
    public void An_orientation_is_written_back_alone_after_the_jfif_segment()
    {
        var plain = Jpegs.Plain();
        var app0 = Jpegs.SegmentLength(plain, 2);
        var source = Jpegs.Insert(plain, 2 + app0, Jpegs.Segment(0xE1, [.. "Exif\0\0"u8, .. ExifOrientationTests.Tiff(8, bigEndian: false)]));

        var stripped = MetadataStripper.StripJpeg(source);

        Assert.Equal(8, ExifOrientation.Read(stripped));
        Assert.Equal(0xE0, stripped[3]); // APP0 still first
        Assert.Equal(0xE1, stripped[2 + app0 + 1]); // the new EXIF right after it
        var exifLength = Jpegs.SegmentLength(stripped, 2 + app0);
        Assert.Equal(4 + 6 + 26, exifLength); // header, Exif\0\0, the 26-byte TIFF: nothing else survived
        Assert.Equal(plain.AsSpan(2 + app0).ToArray(), stripped.AsSpan(2 + app0 + exifLength).ToArray());
    }

    [Fact]
    public void The_stripped_file_decodes_to_the_same_pixels()
    {
        var source = Jpegs.WithApp1(Xmp);

        var stripped = MetadataStripper.StripJpeg(source);

        using var before = JpegDecoder.Decode(source);
        using var after = JpegDecoder.Decode(stripped);
        Assert.Equal(before.Pixels.ToArray(), after.Pixels.ToArray());
    }

    [Fact]
    public void Bytes_that_are_not_a_jpeg_are_refused()
    {
        Assert.Throws<ImageDecodeException>(() => MetadataStripper.StripJpeg([0x89, 0x50, 0x4E, 0x47]));
    }
}
