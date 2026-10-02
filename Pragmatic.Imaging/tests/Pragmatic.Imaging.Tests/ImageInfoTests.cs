using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

public class ImageInfoTests
{
    [NativeRequiredFact]
    public void FromBytes_ValidPng_ReturnsCorrectDimensions()
    {
        var png = TestHelper.CreateTestPng(10, 20);

        var info = ImageInfo.FromBytes(png);

        info.Width.Should().Be(10);
        info.Height.Should().Be(20);
        info.Format.Should().Be(ImageFormat.Png);
    }

    [NativeRequiredFact]
    public void FromBytes_EncodedAsJpeg_DetectsJpegFormat()
    {
        var png = TestHelper.CreateTestPng(8, 8);
        // Re-encode as JPEG via pipeline
        using var pipeline = ImagePipeline.Load(png);
        var jpeg = pipeline.Encode(ImageFormat.Jpeg, quality: 80);

        var info = ImageInfo.FromBytes(jpeg);

        info.Width.Should().Be(8);
        info.Height.Should().Be(8);
        info.Format.Should().Be(ImageFormat.Jpeg);
    }
}
