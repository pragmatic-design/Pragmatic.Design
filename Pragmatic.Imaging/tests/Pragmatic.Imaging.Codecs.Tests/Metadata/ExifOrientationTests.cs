using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Pragmatic.Imaging.Codecs.Metadata;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests.Metadata;

/// <summary>
///     Reading the EXIF orientation, and the transform that shows each of the eight. The transform is
///     checked against the meaning of the tag (TIFF 6.0, Orientation): where the stored image's top-left
///     pixel ends up once shown.
/// </summary>
public class ExifOrientationTests
{
    [Theory]
    [InlineData(1, 3, 2, 0u)] // as stored
    [InlineData(2, 3, 2, 2u)] // mirrored: the stored top-right is shown top-left
    [InlineData(3, 3, 2, 5u)] // half turn: bottom-right
    [InlineData(4, 3, 2, 3u)] // mirrored vertically: bottom-left
    [InlineData(5, 2, 3, 0u)] // transposed: top-left stays
    [InlineData(6, 2, 3, 3u)] // shown turned clockwise: the bottom-left comes to the top-left
    [InlineData(7, 2, 3, 5u)] // transverse: bottom-right
    [InlineData(8, 2, 3, 2u)] // shown turned counter-clockwise: top-right
    public void Each_orientation_shows_the_stored_corner_it_names(int orientation, int width, int height, uint topLeft)
    {
        var image = new PixelBuffer(3, 2);
        var words = MemoryMarshal.Cast<byte, uint>(image.Pixels);
        for (var i = 0; i < words.Length; i++)
            words[i] = (uint)i;

        using var shown = ExifOrientation.Apply(image, orientation);

        Assert.Equal((width, height), (shown.Width, shown.Height));
        Assert.Equal(topLeft, MemoryMarshal.Cast<byte, uint>(shown.Pixels)[0]);
        Assert.Equal(orientation >= 5, ExifOrientation.SwapsAxes(orientation));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_tag_is_read_in_either_byte_order(bool bigEndian)
    {
        var jpeg = Jpegs.WithApp1([.. "Exif\0\0"u8, .. Tiff(6, bigEndian)]);

        Assert.Equal(6, ExifOrientation.Read(jpeg));
    }

    [Fact]
    public void A_file_without_exif_reads_as_stored()
    {
        Assert.Equal(1, ExifOrientation.Read(Jpegs.Plain()));
    }

    [Fact]
    public void The_start_of_a_file_is_enough_and_a_cut_segment_reads_as_absent()
    {
        var jpeg = Jpegs.WithApp1([.. "Exif\0\0"u8, .. Tiff(6, bigEndian: true)]);

        // Cut inside the APP1 segment: nothing to read, and no exception for a prefix.
        Assert.Equal(1, ExifOrientation.Read(jpeg.AsSpan(0, 12)));
        Assert.Equal(6, ExifOrientation.Read(jpeg.AsSpan(0, 60)));
    }

    [Fact]
    public void A_malformed_block_reads_as_stored()
    {
        var tiff = Tiff(6, bigEndian: true);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(4), 5000); // IFD0 past the end

        Assert.Equal(1, ExifOrientation.Read(Jpegs.WithApp1([.. "Exif\0\0"u8, .. tiff])));
    }

    [Fact]
    public void An_xmp_app1_is_not_taken_for_exif()
    {
        var jpeg = Jpegs.WithApp1([.. "http://ns.adobe.com/xap/1.0/\0"u8, .. "<x:xmpmeta/>"u8]);

        Assert.Equal(1, ExifOrientation.Read(jpeg));
    }

    /// <summary>A TIFF block with IFD0 holding only Orientation.</summary>
    internal static byte[] Tiff(int orientation, bool bigEndian)
    {
        var tiff = new byte[26];
        if (bigEndian)
        {
            "MM"u8.CopyTo(tiff);
            BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(2), 42);
            BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(4), 8);
            BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(10), 0x0112);
            BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(12), 3);
            BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(14), 1);
            BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(18), (ushort)orientation);
        }
        else
        {
            "II"u8.CopyTo(tiff);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2), 42);
            BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(10), 0x0112);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(12), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(14), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(18), (ushort)orientation);
        }

        return tiff;
    }
}
