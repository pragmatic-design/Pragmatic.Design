using Pragmatic.Imaging.Codecs.WebP;
using Pragmatic.Testing.Visual;

namespace Pragmatic.Imaging.Codecs.Tests.WebP;

/// <summary>
///     The managed WebP decoder reads every file libwebp wrote to the pixels libwebp reads from it, byte for
///     byte: lossless, lossy, alpha, animation (the first frame) and the extended container. Lossy is exact
///     too, because VP8 reconstruction is defined to the bit and the conversion to RGB is libwebp's.
/// </summary>
public sealed class WebPDecoderTests
{
    [Fact]
    public void TheFiles_AreEachListedWithTheirProvenance() => ReferenceManifest.AssertComplete(WebPFiles.Directory);

    [Theory]
    [MemberData(nameof(Files))]
    public void Decode_OfAFileLibwebpWrote_IsThePixelsLibwebpReads(string file)
    {
        var encoded = WebPFiles.Read(file);
        var (expected, width, height) = SkiaOracle.DecodeRgba(encoded);

        using var decoded = WebPDecoder.Decode(encoded);

        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        var actual = decoded.Pixels.ToArray();
        if (!actual.AsSpan().SequenceEqual(expected))
            Assert.Fail(Describe(actual, expected, width));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Identify_OfAFileLibwebpWrote_IsItsSize(string file)
    {
        var encoded = WebPFiles.Read(file);
        var (_, width, height) = SkiaOracle.DecodeRgba(encoded);

        Assert.Equal((width, height), WebPDecoder.Identify(encoded));
    }

    [Fact]
    public void Decode_OfALossyFileWhoseHeaderOmitsItsAlpha_IsOpaqueLikeLibwebpReadsIt()
    {
        var encoded = WebPFiles.Read("lossy-alpha-q75.webp");
        encoded[20] &= unchecked((byte)~0x10); // the VP8X flags: the alpha bit cleared, the ALPH chunk left in place
        var (expected, _, _) = SkiaOracle.DecodeRgba(encoded);

        using var decoded = WebPDecoder.Decode(encoded);

        Assert.All(expected.Where((_, i) => i % 4 == 3), alpha => Assert.Equal(255, alpha));
        Assert.True(decoded.Pixels.SequenceEqual(expected), "pixels differ from libwebp's reading of the same file");
    }

    [Fact]
    public void Decode_OfAnIndexPastTheColourTable_IsTransparentBlackLikeLibwebpReadsIt()
    {
        var encoded = IndexPastThePalette.File();
        var (expected, width, height) = SkiaOracle.DecodeRgba(encoded);

        using var decoded = WebPDecoder.Decode(encoded);

        Assert.Equal((2, 1), (width, height));
        Assert.Equal([.. IndexPastThePalette.Colour, 0, 0, 0, 0], expected);
        Assert.True(decoded.Pixels.SequenceEqual(expected), "pixels differ from libwebp's reading of the same file");
    }

    public static TheoryData<string> Files() => WebPFiles.All();

    private static string Describe(byte[] actual, byte[] expected, int width)
    {
        var differing = 0;
        var largest = 0;
        var first = -1;
        for (var i = 0; i < actual.Length; i++)
        {
            var difference = Math.Abs(actual[i] - expected[i]);
            if (difference == 0)
                continue;
            differing++;
            largest = Math.Max(largest, difference);
            if (first < 0)
                first = i;
        }

        var pixel = first / 4;
        return $"{differing} of {actual.Length} samples differ, by up to {largest}; the first at ({pixel % width}, {pixel / width}) channel {first % 4}: " +
               $"{actual[first]} where libwebp reads {expected[first]}.";
    }
}
