using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

public class ImageConverterTests
{
    [NativeRequiredFact]
    public async Task ThumbnailAsync_WideImage_PreservesAspectRatio()
    {
        var png = TestHelper.CreateTestPng(200, 100);

        var thumb = await ImageConverter.ThumbnailAsync(png, 50, 50);

        var info = ImageInfo.FromBytes(thumb);
        info.Width.Should().Be(50);
        info.Height.Should().Be(25);
    }

    // --- Sync siblings must mirror the async variants ---

    [NativeRequiredFact]
    public void Thumbnail_Sync_PreservesAspectRatio()
    {
        var png = TestHelper.CreateTestPng(200, 100);

        var thumb = ImageConverter.Thumbnail(png, 50, 50);

        var info = ImageInfo.FromBytes(thumb);
        info.Width.Should().Be(50);
        info.Height.Should().Be(25);
    }

    [NativeRequiredFact]
    public void Convert_Sync_RespectsFormat()
    {
        var png = TestHelper.CreateTestPng(40, 40);

        var webp = ImageConverter.Convert(png, ImageFormat.WebP);

        webp.Should().NotBeEmpty();
        webp[0].Should().Be((byte)'R');
        webp[1].Should().Be((byte)'I');
    }

    [NativeRequiredFact]
    public void Resize_Sync_ProducesExactDimensions()
    {
        var png = TestHelper.CreateTestPng(100, 100);

        var resized = ImageConverter.Resize(png, 30, 20);

        var info = ImageInfo.FromBytes(resized);
        info.Width.Should().Be(30);
        info.Height.Should().Be(20);
    }

    [NativeRequiredFact]
    public void StripExif_Sync_ProducesValidImage()
    {
        var png = TestHelper.CreateTestPng(40, 40);

        var stripped = ImageConverter.StripExif(png);

        stripped.Should().NotBeEmpty();
        var info = ImageInfo.FromBytes(stripped);
        info.Width.Should().Be(40);
        info.Height.Should().Be(40);
    }

    [NativeRequiredFact]
    public async Task ThumbnailAsync_TallImage_PreservesAspectRatio()
    {
        var png = TestHelper.CreateTestPng(100, 200);

        var thumb = await ImageConverter.ThumbnailAsync(png, 50, 50);

        var info = ImageInfo.FromBytes(thumb);
        info.Width.Should().Be(25);
        info.Height.Should().Be(50);
    }

    [NativeRequiredFact]
    public async Task ThumbnailAsync_SquareImage_PreservesAspectRatio()
    {
        var png = TestHelper.CreateTestPng(200, 200);

        var thumb = await ImageConverter.ThumbnailAsync(png, 100, 100);

        var info = ImageInfo.FromBytes(thumb);
        info.Width.Should().Be(100);
        info.Height.Should().Be(100);
    }

    [NativeRequiredFact]
    public async Task ThumbnailAsync_OutputFormat_RespectsFormat()
    {
        var png = TestHelper.CreateTestPng(100, 100);

        var thumb = await ImageConverter.ThumbnailAsync(png, 50, 50, ImageFormat.WebP);

        thumb.Should().NotBeEmpty();
        // WebP RIFF header
        thumb[0].Should().Be((byte)'R');
        thumb[1].Should().Be((byte)'I');
    }

    [NativeRequiredFact]
    public async Task ConvertAsync_PngToJpeg_ProducesValidJpeg()
    {
        var png = TestHelper.CreateTestPng(32, 32);

        var jpeg = await ImageConverter.ConvertAsync(png, ImageFormat.Jpeg, quality: 80);

        jpeg.Should().NotBeEmpty();
        jpeg[0].Should().Be(0xFF);
        jpeg[1].Should().Be(0xD8);
    }

    [NativeRequiredFact]
    public async Task ConvertAsync_PngToWebP_ProducesValidWebP()
    {
        var png = TestHelper.CreateTestPng(32, 32);

        var webp = await ImageConverter.ConvertAsync(png, ImageFormat.WebP);

        webp.Should().NotBeEmpty();
        webp[0].Should().Be((byte)'R');
    }

    [NativeRequiredFact]
    public async Task StripExifAsync_ProducesValidImage()
    {
        var png = TestHelper.CreateTestPng(16, 16);

        var stripped = await ImageConverter.StripExifAsync(png);

        stripped.Should().NotBeEmpty();
        var info = ImageInfo.FromBytes(stripped);
        info.Width.Should().Be(16);
        info.Height.Should().Be(16);
        info.Format.Should().Be(ImageFormat.Png);
    }

    [NativeRequiredFact]
    public async Task ResizeAsync_ExactDimensions()
    {
        var png = TestHelper.CreateTestPng(100, 100);

        var resized = await ImageConverter.ResizeAsync(png, 40, 30);

        var info = ImageInfo.FromBytes(resized);
        info.Width.Should().Be(40);
        info.Height.Should().Be(30);
    }

    [NativeRequiredFact]
    public async Task ThumbnailAsync_Cancellation_ThrowsOperationCanceled()
    {
        var png = TestHelper.CreateTestPng(100, 100);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => ImageConverter.ThumbnailAsync(png, 50, 50, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
