using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests.Exif;

/// <summary>
///     A JPEG or PNG with an EXIF orientation loads the way it is shown, and <see cref="ImageInfo" />
///     reports the size it is shown at, so a thumbnail of a portrait phone photo is a portrait.
/// </summary>
/// <remarks>
///     The fixture is a 32×16 image, red left and blue right. Under orientation 6 (rotate 90° clockwise
///     to show) it is 16×32 with red on top; under 8 (counter-clockwise), blue on top. The other six
///     orientations are the pixel transforms of <c>ExifOrientation.Apply</c>, tested in the core.
/// </remarks>
public class TheDecoderAppliesTheOrientationTests
{
    [Theory]
    [InlineData(ImageFormat.Jpeg)]
    [InlineData(ImageFormat.Png)]
    public void Orientation_6_loads_turned_clockwise(ImageFormat format)
    {
        using var pipeline = ImagePipeline.Load(Fixture(format, 6));

        pipeline.Width.Should().Be((uint)OrientedImages.Height);
        pipeline.Height.Should().Be((uint)OrientedImages.Width);
        var (top, bottom) = UprightPixels.Of(pipeline);
        UprightPixels.IsRed(top).Should().BeTrue();
        UprightPixels.IsBlue(bottom).Should().BeTrue();
    }

    [Theory]
    [InlineData(ImageFormat.Jpeg)]
    [InlineData(ImageFormat.Png)]
    public void Orientation_8_loads_turned_counter_clockwise(ImageFormat format)
    {
        using var pipeline = ImagePipeline.Load(Fixture(format, 8));

        pipeline.Width.Should().Be((uint)OrientedImages.Height);
        var (top, bottom) = UprightPixels.Of(pipeline);
        UprightPixels.IsBlue(top).Should().BeTrue();
        UprightPixels.IsRed(bottom).Should().BeTrue();
    }

    [Theory]
    [InlineData(ImageFormat.Jpeg)]
    [InlineData(ImageFormat.Png)]
    public void Image_info_reports_the_size_the_image_is_shown_at(ImageFormat format)
    {
        var info = ImageInfo.FromBytes(Fixture(format, 6));

        info.Width.Should().Be((uint)OrientedImages.Height);
        info.Height.Should().Be((uint)OrientedImages.Width);
        info.Format.Should().Be(format);
    }

    [Fact]
    public void Without_an_orientation_the_image_loads_as_stored()
    {
        using var pipeline = ImagePipeline.Load(OrientedImages.Jpeg(1));

        pipeline.Width.Should().Be((uint)OrientedImages.Width);
        pipeline.Height.Should().Be((uint)OrientedImages.Height);
    }

    private static byte[] Fixture(ImageFormat format, int orientation)
        => format == ImageFormat.Jpeg ? OrientedImages.Jpeg(orientation) : OrientedImages.Png(orientation);
}
