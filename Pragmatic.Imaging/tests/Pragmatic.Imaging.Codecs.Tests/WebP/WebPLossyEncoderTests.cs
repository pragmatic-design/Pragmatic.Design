using Pragmatic.Imaging.Codecs.Tests.Workload;
using Pragmatic.Imaging.Codecs.WebP;

namespace Pragmatic.Imaging.Codecs.Tests.WebP;

/// <summary>
///     What the lossy encoder writes is a valid VP8 frame: libwebp reads it, to the same pixels as the managed
///     decoder, close to the source, with any alpha kept exactly; at every size, macroblocks cut by the edge
///     included.
/// </summary>
public sealed class WebPLossyEncoderTests
{
    [Theory]
    [InlineData(1, 1, 75)]
    [InlineData(13, 1, 75)]
    [InlineData(1, 13, 75)]
    [InlineData(31, 13, 75)]
    [InlineData(600, 400, 0)]
    [InlineData(600, 400, 50)]
    [InlineData(600, 400, 75)]
    [InlineData(600, 400, 100)]
    public void EncodeLossy_OfASyntheticPhoto_DecodesInLibwebpAsInTheManagedDecoder(int width, int height, int quality)
    {
        var rgba = SyntheticPhoto.Rgba(width, height);

        var (libwebp, managed) = RoundTrip(rgba, width, height, quality);

        Assert.True(libwebp.AsSpan().SequenceEqual(managed), "libwebp and the managed decoder read different pixels");
        Assert.All(Enumerable.Range(0, width * height), p => Assert.Equal(255, libwebp[p * 4 + 3]));
    }

    [Theory]
    [InlineData(50, 30.0)]
    [InlineData(75, 33.0)]
    [InlineData(90, 37.0)]
    public void EncodeLossy_OfASyntheticPhoto_StaysCloseToTheSource(int quality, double minPsnr)
    {
        var rgba = SyntheticPhoto.Rgba(600, 400);

        var (libwebp, _) = RoundTrip(rgba, 600, 400, quality);

        Assert.InRange(Psnr(libwebp, rgba), minPsnr, double.MaxValue);
    }

    [Fact]
    public void EncodeLossy_OfAnImageWithAlpha_KeepsItsAlphaExactly()
    {
        var rgba = SyntheticPhoto.Rgba(97, 61);
        for (var p = 0; p < 97 * 61; p++)
            rgba[p * 4 + 3] = (byte)(p % 97 * 255 / 96);

        var (libwebp, managed) = RoundTrip(rgba, 97, 61, 75);

        for (var p = 0; p < 97 * 61; p++)
        {
            Assert.Equal(rgba[p * 4 + 3], libwebp[p * 4 + 3]);
            Assert.Equal(rgba[p * 4 + 3], managed[p * 4 + 3]);
        }
    }

    [Fact]
    public void EncodeLossy_TheSameImageTwice_WritesTheSameBytes()
    {
        var rgba = SyntheticPhoto.Rgba(120, 80);
        using var image = new PixelBuffer(120, 80);
        rgba.CopyTo(image.Pixels);

        Assert.Equal(WebPEncoder.EncodeLossy(image, 75), WebPEncoder.EncodeLossy(image, 75));
    }

    [Fact]
    public void EncodeLossy_AfterAWiderImageAtAnotherQuality_WritesWhatAFreshEncoderWrites()
    {
        // The encoder keeps its working state from one image to the next on a thread: nothing of the image
        // before may reach the one after. A thread that never encoded starts with nothing.
        using var target = Image(SyntheticPhoto.Rgba(600, 400), 600, 400);
        using var before = Image(SyntheticPhoto.Rgba(904, 312), 904, 312);
        byte[]? fresh = null;
        var thread = new Thread(() => fresh = WebPEncoder.EncodeLossy(target, 50));
        thread.Start();
        thread.Join();

        WebPEncoder.EncodeLossy(before, 0);
        var after = WebPEncoder.EncodeLossy(target, 50);

        Assert.Equal(fresh, after);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void EncodeLossy_AtAQualityOutsideTheRange_Throws(int quality)
    {
        using var image = new PixelBuffer(8, 8);

        Assert.Throws<ArgumentOutOfRangeException>(() => WebPEncoder.EncodeLossy(image, quality));
    }

    private static PixelBuffer Image(byte[] rgba, int width, int height)
    {
        var image = new PixelBuffer(width, height);
        rgba.CopyTo(image.Pixels);
        return image;
    }

    private static (byte[] Libwebp, byte[] Managed) RoundTrip(byte[] rgba, int width, int height, int quality)
    {
        using var image = new PixelBuffer(width, height);
        rgba.CopyTo(image.Pixels);
        var encoded = WebPEncoder.EncodeLossy(image, quality);

        var (libwebp, decodedWidth, decodedHeight) = SkiaOracle.DecodeRgba(encoded);
        Assert.Equal((width, height), (decodedWidth, decodedHeight));
        using var managed = WebPDecoder.Decode(encoded);
        return (libwebp, managed.Pixels.ToArray());
    }

    private static double Psnr(byte[] decoded, byte[] source)
    {
        double sum = 0;
        var count = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (i % 4 == 3)
                continue;
            var d = decoded[i] - source[i];
            sum += d * d;
            count++;
        }

        return 10 * Math.Log10(255.0 * 255.0 / (sum / count));
    }
}
