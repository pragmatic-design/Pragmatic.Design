using Pragmatic.Imaging.Codecs.Transforms;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests.Transforms;

/// <summary>
///     The blur's kernel is the one the <c>image</c> crate builds (its pixel parity with the native library
///     is tested in Pragmatic.Imaging.Tests), a flat image stays flat, and a sigma the crate would panic
///     on is refused as an argument.
/// </summary>
public class GaussianBlurTests
{
    [Theory]
    [InlineData(0.8f, 3)] // the floor of 3
    [InlineData(1.0f, 5)] // 4.33 truncates to 4, even, so 5
    [InlineData(2.0f, 11)]
    [InlineData(3.0f, 17)]
    public void The_kernel_has_the_crates_size_and_sums_to_one(float sigma, int size)
    {
        var kernel = GaussianBlur.Kernel(sigma);

        Assert.Equal(size, kernel.Length);
        Assert.Equal(1f, kernel.Sum(), 4);
        Assert.Equal(kernel[0], kernel[^1]); // symmetric
    }

    [Fact]
    public void A_flat_image_stays_flat()
    {
        using var image = new PixelBuffer(9, 7);
        image.Pixels.Fill(137);

        using var blurred = GaussianBlur.Blur(image, 2.5f);
        using var sharpened = GaussianBlur.Unsharpen(image, 2.5f, 0);

        Assert.All(blurred.Pixels.ToArray(), b => Assert.Equal(137, b));
        Assert.All(sharpened.Pixels.ToArray(), b => Assert.Equal(137, b));
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void A_sigma_that_is_not_a_positive_number_is_refused(float sigma)
    {
        using var image = new PixelBuffer(4, 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => GaussianBlur.Blur(image, sigma));
        Assert.Throws<ArgumentOutOfRangeException>(() => GaussianBlur.Unsharpen(image, sigma, 1));
    }

    [Fact]
    public void Zero_means_the_default_for_blur_and_is_refused_for_sharpen()
    {
        using var image = new PixelBuffer(4, 4);

        GaussianBlur.Blur(image, 0f).Dispose();
        Assert.Throws<ArgumentOutOfRangeException>(() => GaussianBlur.Unsharpen(image, 0f, 1));
    }
}
