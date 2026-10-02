using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

// The enum underlying values are an ABI contract with the Rust native layer
// (FormatCode / ResizeFilterCode). These are pure managed assertions.
public class EnumContractTests
{
    [Theory]
    [InlineData(ImageFormat.Unknown, 0)]
    [InlineData(ImageFormat.Png, 1)]
    [InlineData(ImageFormat.Jpeg, 2)]
    [InlineData(ImageFormat.WebP, 3)]
    [InlineData(ImageFormat.Avif, 4)]
    [InlineData(ImageFormat.Gif, 5)]
    [InlineData(ImageFormat.Bmp, 6)]
    [InlineData(ImageFormat.Tiff, 7)]
    public void ImageFormat_HasStableNativeAbiValues(ImageFormat format, int expected)
    {
        ((int)format).Should().Be(expected);
    }

    [Fact]
    public void ImageFormat_DefaultIsUnknown()
    {
        default(ImageFormat).Should().Be(ImageFormat.Unknown);
    }

    [Theory]
    [InlineData(ResizeFilter.Nearest, 0)]
    [InlineData(ResizeFilter.Triangle, 1)]
    [InlineData(ResizeFilter.CatmullRom, 2)]
    [InlineData(ResizeFilter.Gaussian, 3)]
    [InlineData(ResizeFilter.Lanczos3, 4)]
    public void ResizeFilter_HasStableNativeAbiValues(ResizeFilter filter, int expected)
    {
        ((int)filter).Should().Be(expected);
    }

    [Fact]
    public void ResizeFilter_DefaultIsNearest()
    {
        default(ResizeFilter).Should().Be(ResizeFilter.Nearest);
    }
}
