using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

/// <summary>
///     An image too large for the format asked of it fails as an encode failure of this module, as it did when the
///     native library wrote that format, not with the codec's own argument exception.
/// </summary>
public sealed class EncodeOfAnImageTooLargeForItsFormatTests
{
    [Fact]
    public void EncodeGif_WiderThan65535Pixels_FailsAsAnEncodeFailure()
    {
        using var pipeline = ImagePipeline.Load(TestHelper.CreateTestPng(65536, 1));

        var failure = Assert.Throws<ImagingException>(() => pipeline.Encode(ImageFormat.Gif));

        failure.Reason.Should().Be(ImagingError.EncodeFailed);
    }

    [Fact]
    public void EncodeGif_ToAStream_WiderThan65535Pixels_FailsAsAnEncodeFailure()
    {
        using var pipeline = ImagePipeline.Load(TestHelper.CreateTestPng(65536, 1));
        using var output = new MemoryStream();

        var failure = Assert.Throws<ImagingException>(() => pipeline.EncodeTo(output, ImageFormat.Gif));

        failure.Reason.Should().Be(ImagingError.EncodeFailed);
    }
}
