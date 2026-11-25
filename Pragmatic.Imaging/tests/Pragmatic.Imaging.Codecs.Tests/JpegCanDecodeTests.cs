using Pragmatic.Imaging.Codecs.Jpeg;
using Pragmatic.Imaging.Codecs.Tests.Metadata;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests;

/// <summary>
///     <see cref="JpegDecoder.CanDecode" /> answers from the frame header what <see cref="JpegDecoder.Decode" />
///     would refuse, so a caller can route a CMYK, 12-bit or arithmetic-coded JPEG elsewhere: checked by
///     editing one field of a real frame header at a time.
/// </summary>
public class JpegCanDecodeTests
{
    [Fact]
    public void A_baseline_ycbcr_jpeg_is_decodable()
    {
        Assert.True(JpegDecoder.CanDecode(Jpegs.Plain()));
    }

    [Fact]
    public void Four_components_are_not()
    {
        var jpeg = Jpegs.Plain();
        jpeg[FrameHeader(jpeg) + 4 + 5] = 4;

        Assert.False(JpegDecoder.CanDecode(jpeg));
    }

    [Fact]
    public void Twelve_bit_samples_are_not()
    {
        var jpeg = Jpegs.Plain();
        jpeg[FrameHeader(jpeg) + 4] = 12;

        Assert.False(JpegDecoder.CanDecode(jpeg));
        Assert.Throws<ImageDecodeException>(() => JpegDecoder.Decode(jpeg).Dispose());
    }

    [Theory]
    [InlineData(0xC3)] // lossless
    [InlineData(0xC9)] // arithmetic-coded
    public void Other_frame_types_are_not(byte marker)
    {
        var jpeg = Jpegs.Plain();
        jpeg[FrameHeader(jpeg) + 1] = marker;

        Assert.False(JpegDecoder.CanDecode(jpeg));
        Assert.Throws<ImageDecodeException>(() => JpegDecoder.Decode(jpeg).Dispose());
    }

    /// <summary>The offset of the SOF0 marker's 0xFF.</summary>
    private static int FrameHeader(byte[] jpeg)
    {
        for (var at = 2; at < jpeg.Length; at += Jpegs.SegmentLength(jpeg, at))
        {
            if (jpeg[at + 1] == 0xC0)
                return at;
        }

        throw new InvalidOperationException("The encoder wrote no SOF0.");
    }
}
