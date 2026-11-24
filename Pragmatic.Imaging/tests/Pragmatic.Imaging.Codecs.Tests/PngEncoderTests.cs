using Pragmatic.Imaging.Codecs.Tests.Workload;
using Pragmatic.Imaging.Codecs.Png;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests;

public class PngEncoderTests
{
    [Fact]
    public void An_image_with_transparency_round_trips_through_skia_as_rgba()
    {
        const int width = 203;
        const int height = 77;
        var source = SyntheticPhoto.Rgba(width, height);
        for (var i = 3; i < source.Length; i += 4 * 13)
            source[i] = (byte)(i % 200);

        var png = Encode(source, width, height);
        Assert.Equal(6, png[25]); // IHDR colour type: RGBA

        var (decoded, w, h) = SkiaOracle.DecodeRgba(png);
        Assert.Equal((width, height), (w, h));
        Assert.Equal(source, decoded);
    }

    [Fact]
    public void An_opaque_image_is_written_as_rgb_and_round_trips()
    {
        const int width = 320;
        const int height = 200;
        var source = SyntheticPhoto.Rgba(width, height);

        var png = Encode(source, width, height);
        Assert.Equal(2, png[25]); // IHDR colour type: RGB

        var (decoded, _, _) = SkiaOracle.DecodeRgba(png);
        Assert.Equal(source, decoded);
    }

    [Fact]
    public void Our_decoder_reads_our_encoder()
    {
        const int width = 1000;
        const int height = 3;
        var source = SyntheticPhoto.Rgba(width, height);
        source[3] = 0;

        var png = Encode(source, width, height);
        using var decoded = PngDecoder.Decode(png);

        Assert.True(decoded.Pixels.SequenceEqual(source));
    }

    private static byte[] Encode(byte[] rgba, int width, int height)
    {
        using var image = new PixelBuffer(width, height);
        rgba.CopyTo(image.Pixels);
        using var output = new MemoryStream();
        PngEncoder.Write(image, output);
        return output.ToArray();
    }
}
