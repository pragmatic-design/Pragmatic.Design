using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

/// <summary>
/// Roundtrip tests: encode to format X, then decode and re-encode back.
/// Validates that all supported formats work end-to-end.
/// </summary>
public class FormatRoundtripTests
{
    [NativeRequiredFact]
    public void Roundtrip_Png()
    {
        AssertRoundtrip(ImageFormat.Png);
    }

    [NativeRequiredFact]
    public void Roundtrip_Jpeg()
    {
        AssertRoundtrip(ImageFormat.Jpeg);
    }

    [NativeRequiredFact]
    public void Roundtrip_WebP()
    {
        AssertRoundtrip(ImageFormat.WebP);
    }

    [NativeRequiredFact]
    public void Roundtrip_Bmp()
    {
        AssertRoundtrip(ImageFormat.Bmp);
    }

    [NativeRequiredFact]
    public void Roundtrip_Gif()
    {
        AssertRoundtrip(ImageFormat.Gif);
    }

    [NativeRequiredFact]
    public void Roundtrip_Tiff()
    {
        AssertRoundtrip(ImageFormat.Tiff);
    }

    private static void AssertRoundtrip(ImageFormat format)
    {
        // Start with a known PNG
        var original = TestHelper.CreateTestPng(32, 32);

        // Encode to target format
        using var pipeline1 = ImagePipeline.Load(original);
        var encoded = pipeline1.Encode(format);
        encoded.Should().NotBeEmpty($"encoding to {format} should produce output");

        // Decode back and verify dimensions
        using var pipeline2 = ImagePipeline.Load(encoded);
        pipeline2.Width.Should().Be(32);
        pipeline2.Height.Should().Be(32);

        // Re-encode to PNG for final validation
        var reencoded = pipeline2.Encode(ImageFormat.Png);
        var info = ImageInfo.FromBytes(reencoded);
        info.Width.Should().Be(32);
        info.Height.Should().Be(32);
    }
}
