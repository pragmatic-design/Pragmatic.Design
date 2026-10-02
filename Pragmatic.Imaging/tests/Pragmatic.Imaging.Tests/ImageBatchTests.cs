using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

public class ImageBatchTests
{
    [NativeRequiredFact]
    public async Task ThumbnailsAsync_MultiplePngs_ProducesCorrectSizes()
    {
        var images = new[]
        {
            TestHelper.CreateTestPng(200, 100),
            TestHelper.CreateTestPng(100, 200),
            TestHelper.CreateTestPng(300, 300),
        };

        var thumbs = await ImageBatch.ThumbnailsAsync(images, 50, 50);

        thumbs.Should().HaveCount(3);

        var info0 = ImageInfo.FromBytes(thumbs[0]);
        info0.Width.Should().Be(50);
        info0.Height.Should().Be(25);

        var info1 = ImageInfo.FromBytes(thumbs[1]);
        info1.Width.Should().Be(25);
        info1.Height.Should().Be(50);

        var info2 = ImageInfo.FromBytes(thumbs[2]);
        info2.Width.Should().Be(50);
        info2.Height.Should().Be(50);
    }

    [NativeRequiredFact]
    public async Task ConvertAsync_PngsToWebP_AllValid()
    {
        var images = new[]
        {
            TestHelper.CreateTestPng(32, 32),
            TestHelper.CreateTestPng(64, 64),
        };

        var results = await ImageBatch.ConvertAsync(images, ImageFormat.WebP);

        results.Should().HaveCount(2);
        foreach (var r in results)
        {
            r[0].Should().Be((byte)'R');
            r[1].Should().Be((byte)'I');
        }
    }

    [NativeRequiredFact]
    public async Task ProcessAsync_CustomOperation_AppliesCorrectly()
    {
        var images = new[]
        {
            TestHelper.CreateTestPng(100, 100),
            TestHelper.CreateTestPng(50, 50),
        };

        var results = await ImageBatch.ProcessAsync(images, data =>
        {
            using var p = ImagePipeline.Load(data.Span);
            p.Grayscale();
            return p.Encode(ImageFormat.Png);
        });

        results.Should().HaveCount(2);
        foreach (var r in results)
            r.Should().NotBeEmpty();
    }

    [NativeRequiredFact]
    public async Task ThumbnailsAsync_MaxConcurrency1_StillProcessesAll()
    {
        var images = new[]
        {
            TestHelper.CreateTestPng(100, 100),
            TestHelper.CreateTestPng(100, 100),
            TestHelper.CreateTestPng(100, 100),
        };

        var thumbs = await ImageBatch.ThumbnailsAsync(images, 30, 30, maxConcurrency: 1);

        thumbs.Should().HaveCount(3);
    }

    [NativeRequiredFact]
    public async Task ProcessAsync_Empty_ReturnsEmpty()
    {
        var results = await ImageBatch.ProcessAsync(
            Array.Empty<byte[]>(),
            _ => []);

        results.Should().BeEmpty();
    }

    [NativeRequiredFact]
    public async Task ThumbnailsAsync_Cancellation_ThrowsOperationCanceled()
    {
        var images = new[]
        {
            TestHelper.CreateTestPng(100, 100),
            TestHelper.CreateTestPng(100, 100),
        };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => ImageBatch.ThumbnailsAsync(images, 50, 50, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
