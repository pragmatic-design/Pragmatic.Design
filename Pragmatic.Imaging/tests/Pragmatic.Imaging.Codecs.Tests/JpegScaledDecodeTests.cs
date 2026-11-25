using Pragmatic.Imaging.Codecs.Tests.Workload;
using Pragmatic.Imaging.Codecs.Jpeg;
using Pragmatic.Imaging.Codecs.Transforms;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests;

/// <summary>
///     A reduced decode against the full decode shrunk by the exact kernel: the low-frequency
///     reconstruction is a different low-pass filter, so the two agree to a few decibels, not to the bit.
/// </summary>
public class JpegScaledDecodeTests
{
    [Theory]
    [InlineData(JpegScale.Half, 1224, 816)]
    [InlineData(JpegScale.Quarter, 612, 408)]
    [InlineData(JpegScale.Eighth, 306, 204)]
    public void A_reduced_decode_has_the_reduced_size_and_matches_a_resized_full_decode(JpegScale scale, int width, int height)
    {
        var jpeg = SkiaOracle.EncodeJpegRgba(SyntheticPhoto.Rgba(2448, 1632), 2448, 1632, 90);

        using var reduced = JpegDecoder.Decode(jpeg, scale);
        using var full = JpegDecoder.Decode(jpeg);
        using var resized = Resampler.Resize(full, width, height, ResizeFilter.Lanczos3, exact: true);

        Assert.Equal((width, height), (reduced.Width, reduced.Height));
        var psnr = Psnr(reduced.Pixels, resized.Pixels);
        Assert.True(psnr > 32, $"PSNR between the {scale} decode and the resized full decode is {psnr:F1} dB");
    }

    [Theory]
    [InlineData(JpegScale.Half, 333, 211, 167, 106)]
    [InlineData(JpegScale.Quarter, 333, 211, 84, 53)]
    [InlineData(JpegScale.Eighth, 333, 211, 42, 27)]
    [InlineData(JpegScale.Quarter, 5, 3, 2, 1)]
    public void Odd_sizes_round_up(JpegScale scale, int width, int height, int expectedWidth, int expectedHeight)
    {
        var jpeg = SkiaOracle.EncodeJpegRgba(SyntheticPhoto.Rgba(width, height), width, height, 80);

        using var reduced = JpegDecoder.Decode(jpeg, scale);

        Assert.Equal((expectedWidth, expectedHeight), (reduced.Width, reduced.Height));
    }

    [Fact]
    public void A_greyscale_file_decodes_reduced_too()
    {
        const int width = 120;
        const int height = 90;
        var gray = new byte[width * height];
        for (var i = 0; i < gray.Length; i++)
            gray[i] = (byte)((i % width) * 2);
        var jpeg = SkiaOracle.EncodeJpegGray8(gray, width, height, 95);

        using var reduced = JpegDecoder.Decode(jpeg, JpegScale.Quarter);

        Assert.Equal((30, 23), (reduced.Width, reduced.Height));
        // A horizontal ramp stays a ramp: each reduced sample near the mean of its four source columns.
        var row = reduced.Row(10);
        for (var x = 1; x < 29; x++)
            Assert.True(row[x * 4] >= row[(x - 1) * 4], $"the ramp dips at column {x}");
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
