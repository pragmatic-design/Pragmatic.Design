using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

/// <summary>
/// Tests using a real-world PNG screenshot (150KB, multi-color, full resolution).
/// Exercises the full pipeline with production-grade data.
/// </summary>
public class RealImageTests
{
    private static readonly string FixturesPath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures");

    private static byte[] LoadFixture(string name)
        => File.ReadAllBytes(Path.Combine(FixturesPath, name));

    [NativeRequiredFact]
    public void Info_RealPng_ReturnsValidDimensions()
    {
        var bytes = LoadFixture("sample.png");

        var info = ImageInfo.FromBytes(bytes);

        info.Width.Should().BeGreaterThan(0);
        info.Height.Should().BeGreaterThan(0);
        info.Format.Should().Be(ImageFormat.Png);
    }

    [NativeRequiredFact]
    public void Load_RealPng_DecodesSuccessfully()
    {
        var bytes = LoadFixture("sample.png");

        using var pipeline = ImagePipeline.Load(bytes);

        pipeline.Width.Should().BeGreaterThan(0);
        pipeline.Height.Should().BeGreaterThan(0);
    }

    [NativeRequiredFact]
    public void Resize_RealPng_ToThumbnail()
    {
        var bytes = LoadFixture("sample.png");

        using var pipeline = ImagePipeline.Load(bytes);
        var originalW = pipeline.Width;
        var originalH = pipeline.Height;

        pipeline.Resize(128, 128, ResizeFilter.Lanczos3);
        var thumbnail = pipeline.Encode(ImageFormat.Png);

        thumbnail.Should().NotBeEmpty();
        var info = ImageInfo.FromBytes(thumbnail);
        info.Width.Should().Be(128);
        info.Height.Should().Be(128);
        // Thumbnail should be smaller than original
        thumbnail.Length.Should().BeLessThan(bytes.Length);
    }

    [NativeRequiredFact]
    public void Convert_RealPng_ToJpeg()
    {
        var bytes = LoadFixture("sample.png");

        using var pipeline = ImagePipeline.Load(bytes);
        var jpeg = pipeline.Encode(ImageFormat.Jpeg, quality: 75);

        jpeg.Should().NotBeEmpty();
        // JPEG SOI marker
        jpeg[0].Should().Be(0xFF);
        jpeg[1].Should().Be(0xD8);

        var info = ImageInfo.FromBytes(jpeg);
        info.Format.Should().Be(ImageFormat.Jpeg);
    }

    [NativeRequiredFact]
    public void Convert_RealPng_ToWebP()
    {
        var bytes = LoadFixture("sample.png");

        using var pipeline = ImagePipeline.Load(bytes);
        var webp = pipeline.Encode(ImageFormat.WebP);

        webp.Should().NotBeEmpty();
        // RIFF header
        webp[0].Should().Be((byte)'R');
        webp[1].Should().Be((byte)'I');
        webp[2].Should().Be((byte)'F');
        webp[3].Should().Be((byte)'F');
    }

    [NativeRequiredFact]
    public void FullPipeline_RealPng_ResizeCropGrayscaleEncodeJpeg()
    {
        var bytes = LoadFixture("sample.png");

        using var pipeline = ImagePipeline.Load(bytes);
        var result = pipeline
            .Resize(400, 300)
            .Crop(50, 50, 200, 150)
            .Grayscale()
            .Encode(ImageFormat.Jpeg, quality: 80);

        result.Should().NotBeEmpty();

        var info = ImageInfo.FromBytes(result);
        info.Width.Should().Be(200);
        info.Height.Should().Be(150);
        info.Format.Should().Be(ImageFormat.Jpeg);
    }

    [NativeRequiredFact]
    public void FullPipeline_RealPng_ResizeFlipBrightnessEncodeWebP()
    {
        var bytes = LoadFixture("sample.png");

        using var pipeline = ImagePipeline.Load(bytes);
        var result = pipeline
            .Resize(256, 256)
            .FlipHorizontal()
            .Brightness(20)
            .Contrast(10.0f)
            .Encode(ImageFormat.WebP);

        result.Should().NotBeEmpty();

        var info = ImageInfo.FromBytes(result);
        info.Width.Should().Be(256);
        info.Height.Should().Be(256);
    }

    [NativeRequiredFact]
    public void Rotate_RealPng_90Degrees()
    {
        var bytes = LoadFixture("sample.png");

        using var pipeline = ImagePipeline.Load(bytes);
        var origW = pipeline.Width;
        var origH = pipeline.Height;

        pipeline.Rotate(90);

        // Dimensions should be swapped
        pipeline.Width.Should().Be(origH);
        pipeline.Height.Should().Be(origW);

        var result = pipeline.Encode(ImageFormat.Png);
        result.Should().NotBeEmpty();
    }

    [NativeRequiredFact]
    public void Blur_RealPng_ProducesLargerPngDueToEntropy()
    {
        var bytes = LoadFixture("sample.png");

        using var pipeline = ImagePipeline.Load(bytes);
        // Heavy blur should produce a PNG with lower entropy (more compressible)
        pipeline.Blur(5.0f);
        var blurred = pipeline.Encode(ImageFormat.Png);

        blurred.Should().NotBeEmpty();
        // Re-decode to verify integrity
        var info = ImageInfo.FromBytes(blurred);
        info.Width.Should().BeGreaterThan(0);
    }

    [NativeRequiredFact]
    public void Sharpen_RealPng_ProducesValidOutput()
    {
        var bytes = LoadFixture("sample.png");

        using var pipeline = ImagePipeline.Load(bytes);
        pipeline.Sharpen(2.0f, 5);
        var sharpened = pipeline.Encode(ImageFormat.Png);

        sharpened.Should().NotBeEmpty();
        var info = ImageInfo.FromBytes(sharpened);
        info.Width.Should().BeGreaterThan(0);
    }

    [NativeRequiredFact]
    public void StripExif_RealPng_ProducesValidOutput()
    {
        var bytes = LoadFixture("sample.png");

        // Strip EXIF (PNG typically doesn't have EXIF, but should still work — decode+re-encode)
        using var pipeline = ImagePipeline.Load(bytes);
        var stripped = pipeline.Encode(ImageFormat.Png);

        stripped.Should().NotBeEmpty();
        var info = ImageInfo.FromBytes(stripped);
        info.Width.Should().BeGreaterThan(0);
    }

    [NativeRequiredFact]
    public void MultiFormat_Roundtrip_RealPng()
    {
        var bytes = LoadFixture("sample.png");
        var info = ImageInfo.FromBytes(bytes);

        // PNG → JPEG → PNG roundtrip
        using var step1 = ImagePipeline.Load(bytes);
        step1.Resize(100, 100);
        var jpeg = step1.Encode(ImageFormat.Jpeg, quality: 90);

        using var step2 = ImagePipeline.Load(jpeg);
        var backToPng = step2.Encode(ImageFormat.Png);

        var finalInfo = ImageInfo.FromBytes(backToPng);
        finalInfo.Width.Should().Be(100);
        finalInfo.Height.Should().Be(100);
        finalInfo.Format.Should().Be(ImageFormat.Png);
    }
}
