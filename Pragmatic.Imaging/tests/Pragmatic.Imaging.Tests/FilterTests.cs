using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

public class FilterTests
{
    [NativeRequiredFact]
    public void Grayscale_ProducesValidOutput()
    {
        var png = TestHelper.CreateTestPng(16, 16);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.Grayscale();
        var result = pipeline.Encode(ImageFormat.Png);

        result.Should().NotBeEmpty();
        var info = ImageInfo.FromBytes(result);
        info.Width.Should().Be(16);
        info.Height.Should().Be(16);
    }

    [NativeRequiredFact]
    public void Blur_ProducesValidOutput()
    {
        var png = TestHelper.CreateTestPng(16, 16);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.Blur(2.0f);
        var result = pipeline.Encode(ImageFormat.Png);

        result.Should().NotBeEmpty();
    }

    [NativeRequiredFact]
    public void Sharpen_ProducesValidOutput()
    {
        var png = TestHelper.CreateTestPng(16, 16);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.Sharpen(1.0f, 1);
        var result = pipeline.Encode(ImageFormat.Png);

        result.Should().NotBeEmpty();
    }

    [NativeRequiredFact]
    public void Brightness_ProducesValidOutput()
    {
        var png = TestHelper.CreateTestPng(16, 16);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.Brightness(30);
        var result = pipeline.Encode(ImageFormat.Png);

        result.Should().NotBeEmpty();
    }

    [NativeRequiredFact]
    public void Contrast_ProducesValidOutput()
    {
        var png = TestHelper.CreateTestPng(16, 16);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.Contrast(20.0f);
        var result = pipeline.Encode(ImageFormat.Png);

        result.Should().NotBeEmpty();
    }
}
