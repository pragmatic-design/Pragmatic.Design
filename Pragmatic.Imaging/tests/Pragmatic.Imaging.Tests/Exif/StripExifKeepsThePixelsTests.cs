using Pragmatic.Testing.Assertions;
using SkiaSharp;

namespace Pragmatic.Imaging.Tests.Exif;

/// <summary>
///     <see cref="ImageConverter.StripExif" /> removes the metadata and leaves the image alone: a JPEG's
///     entropy-coded data comes out byte for byte (no decode, no re-encode, no quality lost), and the
///     orientation survives, so a phone photo still comes out upright.
/// </summary>
public class StripExifKeepsThePixelsTests
{
    [Fact]
    public void A_jpeg_keeps_its_entropy_coded_data_byte_for_byte()
    {
        var source = OrientedImages.Jpeg(6);

        var stripped = ImageConverter.StripExif(source);

        var sourceScan = source.AsSpan(OrientedImages.ScanStart(source)).ToArray();
        var strippedScan = stripped.AsSpan(OrientedImages.ScanStart(stripped)).ToArray();
        strippedScan.Should().Equal(sourceScan);
    }

    [Fact]
    public void A_jpeg_loses_its_camera_metadata()
    {
        var source = OrientedImages.Jpeg(6);
        OrientedImages.Contains(source, OrientedImages.CameraMake).Should().BeTrue();

        var stripped = ImageConverter.StripExif(source);

        OrientedImages.Contains(stripped, OrientedImages.CameraMake).Should().BeFalse();
    }

    [Fact]
    public void A_jpeg_keeps_its_orientation_as_an_independent_decoder_reads_it()
    {
        var stripped = ImageConverter.StripExif(OrientedImages.Jpeg(6));

        using var data = SKData.CreateCopy(stripped);
        using var codec = SKCodec.Create(data);
        codec!.EncodedOrigin.Should().Be(SKEncodedOrigin.RightTop);
    }

    [Fact]
    public void A_jpeg_photo_with_orientation_6_comes_out_upright_after_it()
    {
        var stripped = ImageConverter.StripExif(OrientedImages.Jpeg(6));

        using var pipeline = ImagePipeline.Load(stripped);

        pipeline.Width.Should().Be((uint)OrientedImages.Height);
        pipeline.Height.Should().Be((uint)OrientedImages.Width);
        var pixels = UprightPixels.Of(pipeline);
        UprightPixels.IsRed(pixels.Top).Should().BeTrue();
        UprightPixels.IsBlue(pixels.Bottom).Should().BeTrue();
    }

    [Fact]
    public void A_jpeg_without_orientation_gets_no_exif_block_back()
    {
        var stripped = ImageConverter.StripExif(OrientedImages.Jpeg(1));

        OrientedImages.Contains(stripped, "Exif\0\0").Should().BeFalse();
    }

    [Fact]
    public void A_png_loses_its_camera_metadata_and_keeps_its_orientation()
    {
        var source = OrientedImages.Png(6);
        OrientedImages.Contains(source, OrientedImages.CameraMake).Should().BeTrue();

        var stripped = ImageConverter.StripExif(source);

        OrientedImages.Contains(stripped, OrientedImages.CameraMake).Should().BeFalse();
        using var pipeline = ImagePipeline.Load(stripped);
        pipeline.Width.Should().Be((uint)OrientedImages.Height);
        UprightPixels.IsRed(UprightPixels.Of(pipeline).Top).Should().BeTrue();
    }

    [Fact]
    public void A_png_is_its_source_without_the_exif_chunk()
    {
        var source = OrientedImages.Png(1);
        const int afterIhdr = 8 + 25; // where the fixture put eXIf
        var exifChunk = 12 + System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(source.AsSpan(afterIhdr));

        var stripped = ImageConverter.StripExif(source);

        // Without an orientation to keep, nothing is written back: every other byte is the source's.
        byte[] expected = [.. source.AsSpan(0, afterIhdr), .. source.AsSpan(afterIhdr + exifChunk)];
        stripped.Should().Equal(expected);
    }
}
