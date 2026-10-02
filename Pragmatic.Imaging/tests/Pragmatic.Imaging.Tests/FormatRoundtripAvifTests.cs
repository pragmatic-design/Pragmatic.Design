using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

/// <summary>
/// AVIF format coverage, complementing the codecs exercised end-to-end in
/// <see cref="FormatRoundtripTests"/>.
///
/// The shipped native binary can <em>encode</em> AVIF but is built without AVIF
/// <em>decode</em> support (the AV1 decoder is a heavyweight optional feature of
/// the underlying image library). The observable contract today is therefore an
/// asymmetric one: <see cref="ImagePipeline.Encode"/> to AVIF succeeds, while
/// loading those bytes back throws a clean <see cref="ImagingException"/> rather
/// than corrupting data or crashing. These tests pin that contract so a future
/// build that enables AVIF decode makes the round-trip expectation fail loudly
/// and forces this file to be upgraded to a full roundtrip.
/// </summary>
public class FormatRoundtripAvifTests
{
    [NativeRequiredFact]
    public void Encode_Avif_ProducesNonEmptyOutput()
    {
        var original = TestHelper.CreateTestPng(32, 32);

        using var pipeline = ImagePipeline.Load(original);
        var encoded = pipeline.Encode(ImageFormat.Avif);

        encoded.Should().NotBeEmpty("encoding to AVIF should produce output");
    }

    [NativeRequiredFact]
    public void Encode_Avif_WithExplicitQuality_ProducesNonEmptyOutput()
    {
        var original = TestHelper.CreateTestPng(16, 16);

        using var pipeline = ImagePipeline.Load(original);
        var encoded = pipeline.Encode(ImageFormat.Avif, quality: 50);

        encoded.Should().NotBeEmpty("AVIF encoding ignores quality but must still produce output");
    }

    [NativeRequiredFact]
    public void Load_Avif_OnShippedBinary_ThrowsImagingException()
    {
        var original = TestHelper.CreateTestPng(32, 32);
        using var encoder = ImagePipeline.Load(original);
        var avif = encoder.Encode(ImageFormat.Avif);

        var load = () => ImagePipeline.Load(avif);

        load.Should()
            .Throw<ImagingException>("the shipped native binary is built without AVIF decode support")
            .WithMessage("*Avif*");
    }
}
