using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

public class ImagePipelineTests
{
    [NativeRequiredFact]
    public void Load_ValidPng_DecodesSuccessfully()
    {
        var png = TestHelper.CreateTestPng(16, 16);

        using var pipeline = ImagePipeline.Load(png);

        pipeline.Width.Should().Be(16);
        pipeline.Height.Should().Be(16);
    }

    [NativeRequiredFact]
    public void Resize_DownscalesImage()
    {
        var png = TestHelper.CreateTestPng(100, 100);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.Resize(50, 50);

        pipeline.Width.Should().Be(50);
        pipeline.Height.Should().Be(50);
    }

    [NativeRequiredFact]
    public void Resize_UpscalesImage()
    {
        var png = TestHelper.CreateTestPng(10, 10);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.Resize(40, 40, ResizeFilter.Nearest);

        pipeline.Width.Should().Be(40);
        pipeline.Height.Should().Be(40);
    }

    [NativeRequiredFact]
    public void Crop_ExtractsRegion()
    {
        var png = TestHelper.CreateTestPng(100, 100);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.Crop(10, 10, 30, 20);

        pipeline.Width.Should().Be(30);
        pipeline.Height.Should().Be(20);
    }

    [NativeRequiredFact]
    public void Rotate90_SwapsDimensions()
    {
        var png = TestHelper.CreateTestPng(40, 20);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.Rotate(90);

        pipeline.Width.Should().Be(20);
        pipeline.Height.Should().Be(40);
    }

    [NativeRequiredFact]
    public void Rotate180_PreservesDimensions()
    {
        var png = TestHelper.CreateTestPng(30, 20);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.Rotate(180);

        pipeline.Width.Should().Be(30);
        pipeline.Height.Should().Be(20);
    }

    [NativeRequiredFact]
    public void FlipHorizontal_PreservesDimensions()
    {
        var png = TestHelper.CreateTestPng(16, 16);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.FlipHorizontal();

        pipeline.Width.Should().Be(16);
        pipeline.Height.Should().Be(16);
    }

    [NativeRequiredFact]
    public void FlipVertical_PreservesDimensions()
    {
        var png = TestHelper.CreateTestPng(16, 16);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.FlipVertical();

        pipeline.Width.Should().Be(16);
        pipeline.Height.Should().Be(16);
    }

    [NativeRequiredFact]
    public void Encode_ProducesValidPng()
    {
        var png = TestHelper.CreateTestPng(8, 8);

        using var pipeline = ImagePipeline.Load(png);
        var encoded = pipeline.Encode(ImageFormat.Png);

        encoded.Should().NotBeEmpty();
        // PNG magic bytes
        encoded[0].Should().Be(0x89);
        encoded[1].Should().Be(0x50); // 'P'
        encoded[2].Should().Be(0x4E); // 'N'
        encoded[3].Should().Be(0x47); // 'G'
    }

    [NativeRequiredFact]
    public void Encode_ProducesValidJpeg()
    {
        var png = TestHelper.CreateTestPng(8, 8);

        using var pipeline = ImagePipeline.Load(png);
        var jpeg = pipeline.Encode(ImageFormat.Jpeg, quality: 75);

        jpeg.Should().NotBeEmpty();
        // JPEG magic bytes (SOI marker)
        jpeg[0].Should().Be(0xFF);
        jpeg[1].Should().Be(0xD8);
    }

    [NativeRequiredFact]
    public void Encode_ProducesValidWebP()
    {
        var png = TestHelper.CreateTestPng(8, 8);

        using var pipeline = ImagePipeline.Load(png);
        var webp = pipeline.Encode(ImageFormat.WebP);

        webp.Should().NotBeEmpty();
        // WebP RIFF header
        webp[0].Should().Be((byte)'R');
        webp[1].Should().Be((byte)'I');
        webp[2].Should().Be((byte)'F');
        webp[3].Should().Be((byte)'F');
    }

    [NativeRequiredFact]
    public void FluentChain_Resize_Grayscale_Encode()
    {
        var png = TestHelper.CreateTestPng(64, 64);

        using var pipeline = ImagePipeline.Load(png);
        var result = pipeline
            .Resize(32, 32)
            .Grayscale()
            .Encode(ImageFormat.Png);

        result.Should().NotBeEmpty();

        var info = ImageInfo.FromBytes(result);
        info.Width.Should().Be(32);
        info.Height.Should().Be(32);
    }

    [NativeRequiredFact]
    public void EncodeTo_WritesToStream()
    {
        var png = TestHelper.CreateTestPng(8, 8);

        using var pipeline = ImagePipeline.Load(png);
        using var ms = new MemoryStream();
        pipeline.EncodeTo(ms, ImageFormat.Png);

        ms.Length.Should().BeGreaterThan(0);
    }

    [NativeRequiredFact]
    public void Load_FromStream_Works()
    {
        var png = TestHelper.CreateTestPng(16, 16);
        using var ms = new MemoryStream(png);

        using var pipeline = ImagePipeline.Load(ms);

        pipeline.Width.Should().Be(16);
        pipeline.Height.Should().Be(16);
    }
}
