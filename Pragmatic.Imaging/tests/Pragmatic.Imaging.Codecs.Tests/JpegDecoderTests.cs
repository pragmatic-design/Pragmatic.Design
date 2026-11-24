using Pragmatic.Imaging.Codecs.Tests.Workload;
using Pragmatic.Imaging.Codecs.Jpeg;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests;

/// <summary>
///     Two decoders of the same JPEG differ by their IDCT rounding and chroma upsampling, never by more
///     than a level or two: the oracle is a PSNR floor against Skia's decode, high enough that a wrong
///     block, a swapped channel or a shifted row would fail it.
/// </summary>
public class JpegDecoderTests
{
    [Theory]
    [InlineData(2448, 1632, 90)] // the benchmark's 4 MP photo: 4:2:0 chroma, many MCUs
    [InlineData(333, 211, 75)] // odd sizes: partial MCUs on both edges
    [InlineData(8, 8, 100)] // one MCU
    [InlineData(17, 3, 50)] // narrower than one MCU row
    public void Decodes_a_baseline_jpeg_written_by_skia_close_to_skias_own_decode(int width, int height, int quality)
    {
        var jpeg = SkiaOracle.EncodeJpegRgba(SyntheticPhoto.Rgba(width, height), width, height, quality);
        var (expected, _, _) = SkiaOracle.DecodeRgba(jpeg);

        using var decoded = JpegDecoder.Decode(jpeg);

        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        var psnr = Psnr(decoded.Pixels, expected);
        Assert.True(psnr > 40, $"PSNR against Skia's decode is {psnr:F1} dB");
    }

    [Fact]
    public void Decodes_a_greyscale_jpeg()
    {
        const int width = 120;
        const int height = 90;
        var gray = new byte[width * height];
        for (var i = 0; i < gray.Length; i++)
            gray[i] = (byte)((i % width) * 2);

        var jpeg = SkiaOracle.EncodeJpegGray8(gray, width, height, 95);
        var (expected, _, _) = SkiaOracle.DecodeRgba(jpeg);

        using var decoded = JpegDecoder.Decode(jpeg);

        var psnr = Psnr(decoded.Pixels, expected);
        Assert.True(psnr > 40, $"PSNR against Skia's decode is {psnr:F1} dB");
    }

    [Fact]
    public void Rejects_bytes_that_are_not_a_jpeg()
    {
        Assert.Throws<ImageDecodeException>(() => JpegDecoder.Decode(new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 }));
    }

    [Fact]
    public void Rejects_a_file_truncated_before_the_frame_header()
    {
        var jpeg = SkiaOracle.EncodeJpegRgba(SyntheticPhoto.Rgba(64, 64), 64, 64, 80);
        Assert.Throws<ImageDecodeException>(() => JpegDecoder.Decode(jpeg[..40]));
    }

    private static double Psnr(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        Assert.Equal(a.Length, b.Length);
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            double d = a[i] - b[i];
            sum += d * d;
        }

        var mse = sum / a.Length;
        return mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse);
    }
}
