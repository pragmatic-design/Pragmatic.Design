using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

public class ImageInfoStreamTests
{
    [NativeRequiredFact]
    public void FromStream_ReturnsCorrectInfo()
    {
        var png = TestHelper.CreateTestPng(32, 24);
        using var stream = new MemoryStream(png);

        var info = ImageInfo.FromStream(stream);

        info.Width.Should().Be(32);
        info.Height.Should().Be(24);
        info.Format.Should().Be(ImageFormat.Png);
    }

    [NativeRequiredFact]
    public async Task FromStreamAsync_ReturnsCorrectInfo()
    {
        var png = TestHelper.CreateTestPng(32, 24);
        using var stream = new MemoryStream(png);

        var info = await ImageInfo.FromStreamAsync(stream);

        info.Width.Should().Be(32);
        info.Height.Should().Be(24);
        info.Format.Should().Be(ImageFormat.Png);
    }
}
