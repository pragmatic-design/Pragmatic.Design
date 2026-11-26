using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

/// <summary>
///     <see cref="ImagePipeline.EncodeWebP" /> writes what it is asked for: lossless is what
///     <see cref="ImageFormat.WebP" /> writes, lossy is the managed lossy encoder's file at the quality given,
///     to a stream as to an array. Lossy is never what a call that names no encoding gets.
/// </summary>
public sealed class EncodeWebPWritesTheEncodingAskedForTests
{
    [Fact]
    public void EncodeWebP_Lossless_IsWhatEncodeOfWebPWrites()
    {
        using var pipeline = ImagePipeline.Load(Png());

        pipeline.EncodeWebP(WebPEncoding.Lossless).Should().Equal(pipeline.Encode(ImageFormat.WebP));
    }

    [Fact]
    public void EncodeWebP_Lossy_IsTheManagedLossyEncodersFileAtThatQuality()
    {
        using var source = Exif.OrientedImages.Pixels();
        var reference = Codecs.WebP.WebPEncoder.EncodeLossy(source, 60);
        using var pipeline = ImagePipeline.Load(Png());

        var encoded = pipeline.EncodeWebP(WebPEncoding.Lossy, quality: 60);

        encoded.Should().Equal(reference);
        System.Text.Encoding.ASCII.GetString(encoded, 12, 4).Should().Be("VP8 ");
    }

    [Fact]
    public void EncodeWebPTo_Lossy_WritesTheSameFileToTheStream()
    {
        using var pipeline = ImagePipeline.Load(Png());
        using var output = new MemoryStream();

        pipeline.EncodeWebPTo(output, WebPEncoding.Lossy, quality: 60);

        output.ToArray().Should().Equal(pipeline.EncodeWebP(WebPEncoding.Lossy, quality: 60));
    }

    [Fact]
    public void EncodeWebP_AboveQuality100_Throws()
    {
        using var pipeline = ImagePipeline.Load(Png());

        var encode = () => pipeline.EncodeWebP(WebPEncoding.Lossy, quality: 101);

        encode.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EncodeWebP_OfAnEncodingThatIsNotOne_Throws()
    {
        using var pipeline = ImagePipeline.Load(Png());

        var encode = () => pipeline.EncodeWebP((WebPEncoding)7);

        encode.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static byte[] Png()
    {
        using var source = Exif.OrientedImages.Pixels();
        using var png = new MemoryStream();
        Codecs.Png.PngEncoder.Write(source, png);
        return png.ToArray();
    }
}
