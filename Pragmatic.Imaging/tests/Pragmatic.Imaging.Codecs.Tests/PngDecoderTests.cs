using Pragmatic.Imaging.Codecs.Tests.Workload;
using Pragmatic.Imaging.Codecs.Png;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests;

public class PngDecoderTests
{
    [Fact]
    public void Decodes_an_rgba_png_written_by_skia_to_the_same_pixels()
    {
        const int width = 257;
        const int height = 131;
        var source = SyntheticPhoto.Rgba(width, height);
        // A few translucent pixels, so the file is RGBA and not demoted to RGB by the writer.
        for (var i = 3; i < source.Length; i += 4 * 97)
            source[i] = (byte)(i % 251);

        var png = SkiaOracle.EncodePngRgba(source, width, height);
        var (expected, _, _) = SkiaOracle.DecodeRgba(png);

        using var decoded = PngDecoder.Decode(png);

        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        Assert.True(decoded.Pixels.SequenceEqual(expected), "pixels differ from Skia's decode of the same file");
        Assert.True(decoded.Pixels.SequenceEqual(source), "pixels differ from the source Skia encoded");
    }

    [Fact]
    public void Decodes_a_gray8_png_written_by_skia_as_rgba()
    {
        const int width = 64;
        const int height = 40;
        var gray = new byte[width * height];
        for (var i = 0; i < gray.Length; i++)
            gray[i] = (byte)(i * 7);

        var png = SkiaOracle.EncodePngGray8(gray, width, height);
        Assert.Equal(0, png[25]); // IHDR colour type: Skia wrote a greyscale file, so the test covers that path.

        using var decoded = PngDecoder.Decode(png);

        var pixels = decoded.Pixels;
        for (var i = 0; i < gray.Length; i++)
        {
            Assert.Equal(gray[i], pixels[i * 4]);
            Assert.Equal(gray[i], pixels[i * 4 + 1]);
            Assert.Equal(gray[i], pixels[i * 4 + 2]);
            Assert.Equal(255, pixels[i * 4 + 3]);
        }
    }

    [Fact]
    public void A_tall_image_that_spans_several_staging_refills_decodes_whole()
    {
        // 1300 bytes per RGBA row against a 256 KB staging buffer: dozens of refills, most of them
        // landing in the middle of a row.
        const int width = 325;
        const int height = 4000;
        var source = SyntheticPhoto.Rgba(width, height);
        for (var i = 3; i < source.Length; i += 4 * 131)
            source[i] = (byte)(i % 251);

        var png = SkiaOracle.EncodePngRgba(source, width, height);

        using var decoded = PngDecoder.Decode(png);

        Assert.True(decoded.Pixels.SequenceEqual(source), "pixels differ from the source Skia encoded");
    }

    [Fact]
    public void Rejects_a_chunk_whose_crc_does_not_match()
    {
        var png = SkiaOracle.EncodePngRgba(SyntheticPhoto.Rgba(8, 8), 8, 8);
        png[16] ^= 0x01; // first byte of IHDR's data: the width

        var error = Assert.Throws<ImageDecodeException>(() => PngDecoder.Decode(png));
        Assert.Contains("CRC", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_bytes_that_are_not_a_png()
    {
        Assert.Throws<ImageDecodeException>(() => PngDecoder.Decode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
    }

    [Fact]
    public void Rejects_a_file_truncated_inside_the_image_data()
    {
        var png = SkiaOracle.EncodePngRgba(SyntheticPhoto.Rgba(64, 64), 64, 64);
        var truncated = png[..(png.Length / 2)];

        Assert.Throws<ImageDecodeException>(() => PngDecoder.Decode(truncated));
    }
}
