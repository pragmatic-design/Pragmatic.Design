using Pragmatic.Imaging.Codecs.Tests.Workload;
using Pragmatic.Imaging.Codecs.Jpeg;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests;

public class JpegEncoderTests
{
    [Theory]
    [InlineData(400, 267, 85)] // the benchmark's thumbnail
    [InlineData(333, 211, 90)] // partial MCUs on both edges
    [InlineData(16, 16, 100)] // one MCU
    [InlineData(5, 3, 60)] // smaller than one MCU
    public void Skia_decodes_what_we_encode_close_to_the_source(int width, int height, int quality)
    {
        var source = SyntheticPhoto.Rgba(width, height);
        var jpeg = Encode(source, width, height, quality);

        var (decoded, w, h) = SkiaOracle.DecodeRgba(jpeg);

        Assert.Equal((width, height), (w, h));
        var psnr = Psnr(decoded, source);
        // Lossy, 4:2:0: what a JPEG of this quality looks like against its source. Skia's own encode of
        // the same pixels at the same quality is the yardstick, within a decibel or two.
        var skiaPsnr = Psnr(SkiaOracle.DecodeRgba(SkiaOracle.EncodeJpegRgba(source, width, height, quality)).Rgba, source);
        Assert.True(psnr > skiaPsnr - 2, $"PSNR to source is {psnr:F1} dB; Skia's own encode gets {skiaPsnr:F1} dB");
    }

    [Fact]
    public void Our_decoder_reads_our_encoder_as_skia_does()
    {
        const int width = 640;
        const int height = 480;
        var jpeg = Encode(SyntheticPhoto.Rgba(width, height), width, height, 80);

        var (theirs, _, _) = SkiaOracle.DecodeRgba(jpeg);
        using var ours = JpegDecoder.Decode(jpeg);

        var psnr = Psnr(ours.Pixels, theirs);
        Assert.True(psnr > 40, $"PSNR between the two decodes is {psnr:F1} dB");
    }

    [Fact]
    public void The_file_is_no_larger_than_skias_at_the_same_quality()
    {
        const int width = 400;
        const int height = 267;
        var source = SyntheticPhoto.Rgba(width, height);

        var ours = Encode(source, width, height, 85).Length;
        var theirs = SkiaOracle.EncodeJpegRgba(source, width, height, 85).Length;

        // Fitted Huffman tables against standard ones, the same quantisers: ours should not be bigger.
        Assert.True(ours <= theirs * 1.05, $"ours {ours} bytes, Skia {theirs} bytes");
    }

    [Theory]
    [InlineData(400, 267, 85)]
    [InlineData(333, 211, 90)]
    [InlineData(5, 3, 60)]
    public void Single_pass_through_the_standard_tables_decodes_to_the_same_pixels(int width, int height, int quality)
    {
        var source = SyntheticPhoto.Rgba(width, height);
        var optimized = Encode(source, width, height, quality);
        var singlePass = Encode(source, width, height, quality, optimizeCoding: false);

        // Same quantised coefficients, different Huffman codes: identical pixels out, a larger file.
        var (a, _, _) = SkiaOracle.DecodeRgba(optimized);
        var (b, _, _) = SkiaOracle.DecodeRgba(singlePass);
        Assert.Equal(a, b);
        Assert.True(singlePass.Length >= optimized.Length, $"single pass {singlePass.Length} bytes, optimised {optimized.Length}");
    }

    private static byte[] Encode(byte[] rgba, int width, int height, int quality, bool optimizeCoding = true)
    {
        using var image = new PixelBuffer(width, height);
        rgba.CopyTo(image.Pixels);
        using var output = new MemoryStream();
        JpegEncoder.Write(image, output, quality, optimizeCoding);
        return output.ToArray();
    }

    private static double Psnr(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        Assert.Equal(a.Length, b.Length);
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            // Alpha is 255 on both sides; compare colour only.
            if (i % 4 == 3)
                continue;
            double d = a[i] - b[i];
            sum += d * d;
        }

        var mse = sum / (a.Length / 4 * 3);
        return mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse);
    }
}
