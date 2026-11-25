using Pragmatic.Testing.Assertions;
using SkiaSharp;

namespace Pragmatic.Imaging.Tests.Exif;

/// <summary>
///     The oriented fixtures are what they claim, according to a decoder that is not ours: SkiaSharp
///     reads the orientation from the JPEG's EXIF. Without this, every orientation test below could pass
///     against a malformed block that only this repository's reader accepts.
/// </summary>
public class ExifFixtureTests
{
    [Theory]
    [InlineData(1, SKEncodedOrigin.TopLeft)]
    [InlineData(3, SKEncodedOrigin.BottomRight)]
    [InlineData(6, SKEncodedOrigin.RightTop)]
    [InlineData(8, SKEncodedOrigin.LeftBottom)]
    public void Skia_reads_the_orientation_the_fixture_records(int orientation, SKEncodedOrigin expected)
    {
        using var data = SKData.CreateCopy(OrientedImages.Jpeg(orientation));
        using var codec = SKCodec.Create(data);

        codec.Should().NotBeNull();
        codec!.EncodedOrigin.Should().Be(expected);
        codec.Info.Width.Should().Be(OrientedImages.Width);
    }
}
