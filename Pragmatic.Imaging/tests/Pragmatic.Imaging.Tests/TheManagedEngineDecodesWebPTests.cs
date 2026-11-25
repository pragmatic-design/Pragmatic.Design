using Pragmatic.Imaging.Codecs.Tests.WebP;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

/// <summary>
///     WebP is read by the managed decoder, not the native library. On every file libwebp wrote the two
///     agree to the byte (<see cref="Parity.TheWebPDecoderMatchesTheNativeOneTests" />), so the pixels cannot
///     tell which one ran; what does is the reading of a file that is not right, which each engine words its
///     own way.
/// </summary>
public class TheManagedEngineDecodesWebPTests
{
    [Fact]
    public void A_truncated_webp_is_refused_with_the_managed_decoders_reason()
    {
        var file = IndexPastThePalette.File();
        var truncated = file.AsSpan(0, file.Length - 3).ToArray();

        var load = () => ImagePipeline.Load(truncated);

        var error = load.Should().Throw<ImagingException>().Which;
        error.Reason.Should().Be(ImagingError.DecodeFailed);
        error.Message.Should().StartWith("WebP: ");
    }

    [Fact]
    public void A_webp_encode_is_the_managed_lossless_encoders_output()
    {
        using var source = Exif.OrientedImages.Pixels();
        using var png = new MemoryStream();
        Codecs.Png.PngEncoder.Write(source, png);
        using var reference = new MemoryStream();
        Codecs.WebP.WebPEncoder.EncodeLossless(source, reference);

        using var pipeline = ImagePipeline.Load(png.ToArray());
        var encoded = pipeline.Encode(ImageFormat.WebP, quality: 80);

        encoded.Should().Equal(reference.ToArray());
    }

    [Fact]
    public void A_webp_header_is_read_by_the_managed_decoder()
    {
        var file = IndexPastThePalette.File();
        "ICCP"u8.CopyTo(file.AsSpan(12)); // the first chunk is now one that holds no image

        var identify = () => ImageInfo.FromBytes(file);

        var error = identify.Should().Throw<ImagingException>().Which;
        error.Reason.Should().Be(ImagingError.DecodeFailed);
        error.Message.Should().StartWith("WebP: ");
    }
}
