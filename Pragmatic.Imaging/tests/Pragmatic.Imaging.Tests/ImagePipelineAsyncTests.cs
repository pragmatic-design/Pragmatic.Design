using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

public class ImagePipelineAsyncTests
{
    [NativeRequiredFact]
    public async Task LoadAsync_FromMemory_DecodesSuccessfully()
    {
        var png = TestHelper.CreateTestPng(16, 16);
        ReadOnlyMemory<byte> memory = png;

        await using var pipeline = await ImagePipeline.LoadAsync(memory);

        pipeline.Width.Should().Be(16);
        pipeline.Height.Should().Be(16);
    }

    [NativeRequiredFact]
    public async Task LoadAsync_FromStream_DecodesSuccessfully()
    {
        var png = TestHelper.CreateTestPng(16, 16);
        using var stream = new MemoryStream(png);

        await using var pipeline = await ImagePipeline.LoadAsync(stream);

        pipeline.Width.Should().Be(16);
        pipeline.Height.Should().Be(16);
    }

    [NativeRequiredFact]
    public async Task EncodeAsync_ProducesValidPng()
    {
        var png = TestHelper.CreateTestPng(8, 8);

        await using var pipeline = await ImagePipeline.LoadAsync((ReadOnlyMemory<byte>)png);
        var encoded = await pipeline.EncodeAsync(ImageFormat.Png);

        encoded.Should().NotBeEmpty();
        encoded[0].Should().Be(0x89);
        encoded[1].Should().Be(0x50);
    }

    [NativeRequiredFact]
    public async Task EncodeToStreamAsync_WritesToStream()
    {
        var png = TestHelper.CreateTestPng(8, 8);

        await using var pipeline = await ImagePipeline.LoadAsync((ReadOnlyMemory<byte>)png);
        using var ms = new MemoryStream();
        await pipeline.EncodeToStreamAsync(ms, ImageFormat.Png);

        ms.Length.Should().BeGreaterThan(0);
    }

    [NativeRequiredFact]
    public async Task LoadAsync_Cancellation_ThrowsOperationCanceled()
    {
        var png = TestHelper.CreateTestPng(8, 8);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => ImagePipeline.LoadAsync((ReadOnlyMemory<byte>)png, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [NativeRequiredFact]
    public void Thumbnail_WideImage_FitsWithinBounds()
    {
        var png = TestHelper.CreateTestPng(200, 100);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.Thumbnail(100, 100);

        // 200x100 → ratio = min(100/200, 100/100) = 0.5 → 100x50
        pipeline.Width.Should().Be(100);
        pipeline.Height.Should().Be(50);
    }

    [NativeRequiredFact]
    public void Thumbnail_TallImage_FitsWithinBounds()
    {
        var png = TestHelper.CreateTestPng(100, 200);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.Thumbnail(100, 100);

        // 100x200 → ratio = min(100/100, 100/200) = 0.5 → 50x100
        pipeline.Width.Should().Be(50);
        pipeline.Height.Should().Be(100);
    }

    [NativeRequiredFact]
    public void Thumbnail_SquareImage_FitsWithinBounds()
    {
        var png = TestHelper.CreateTestPng(200, 200);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.Thumbnail(100, 100);

        pipeline.Width.Should().Be(100);
        pipeline.Height.Should().Be(100);
    }

    [NativeRequiredFact]
    public void Thumbnail_SmallerThanBounds_NoUpscale()
    {
        var png = TestHelper.CreateTestPng(50, 30);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.Thumbnail(100, 100);

        // An image already smaller than the bounds must NOT be upscaled.
        // 50x30 with bounds 100x100 → ratio clamped to 1.0 → stays 50x30.
        pipeline.Width.Should().Be(50);
        pipeline.Height.Should().Be(30);
    }

    [NativeRequiredFact]
    public void Thumbnail_SmallerThanBounds_AllowUpscale_Upscales()
    {
        var png = TestHelper.CreateTestPng(50, 30);

        using var pipeline = ImagePipeline.Load(png);
        pipeline.Thumbnail(100, 100, allowUpscale: true);

        // 50x30 → ratio = min(100/50, 100/30) = 2.0 → 100x60 when upscaling is explicitly allowed.
        pipeline.Width.Should().Be(100);
        pipeline.Height.Should().Be(60);
    }

    [NativeRequiredFact]
    public void Thumbnail_ZeroDimension_Throws()
    {
        var png = TestHelper.CreateTestPng(100, 100);

        using var pipeline = ImagePipeline.Load(png);

        var act = () => pipeline.Thumbnail(0, 100);
        act.Should().Throw<ArgumentException>();
    }

    [NativeRequiredFact]
    public async Task FullAsyncPipeline_LoadThumbnailEncodeStream()
    {
        var png = TestHelper.CreateTestPng(200, 100);
        using var input = new MemoryStream(png);

        await using var pipeline = await ImagePipeline.LoadAsync(input);
        pipeline.Thumbnail(50, 50).Grayscale();

        using var output = new MemoryStream();
        await pipeline.EncodeToStreamAsync(output, ImageFormat.WebP, quality: 80);

        output.Length.Should().BeGreaterThan(0);
    }
}
