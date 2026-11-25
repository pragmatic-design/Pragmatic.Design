using Pragmatic.Imaging.Codecs.Tests.Workload;
using Pragmatic.Imaging.Codecs.WebP;

namespace Pragmatic.Imaging.Codecs.Tests.WebP;

/// <summary>
///     What the lossless encoder writes, libwebp reads back to the pixels it was given, byte for byte, and
///     so does the managed decoder: photographs, noise, flat colour, alpha with colour under transparent
///     pixels, and images one pixel wide or high.
/// </summary>
public sealed class WebPEncoderTests
{
    [Theory]
    [MemberData(nameof(Photographs))]
    public void EncodeLossless_OfAPhotograph_DecodesInLibwebpToTheSamePixels(string file)
    {
        using var source = WebPDecoder.Decode(WebPFiles.Read(file));

        AssertRoundTrip(source.Pixels.ToArray(), source.Width, source.Height);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(13, 1)]
    [InlineData(1, 13)]
    [InlineData(31, 13)]
    [InlineData(600, 400)]
    public void EncodeLossless_OfASyntheticPhoto_DecodesInLibwebpToTheSamePixels(int width, int height) =>
        AssertRoundTrip(SyntheticPhoto.Rgba(width, height), width, height);

    [Fact]
    public void EncodeLossless_OfNoiseWithAlpha_KeepsEveryChannelOfEveryPixel()
    {
        var rgba = new byte[257 * 131 * 4];
        new Random(853).NextBytes(rgba);

        AssertRoundTrip(rgba, 257, 131);
    }

    [Fact]
    public void EncodeLossless_OfTransparentPixels_KeepsTheColourUnderThem()
    {
        var rgba = SyntheticPhoto.Rgba(64, 48);
        for (var i = 3; i < rgba.Length; i += 8)
            rgba[i] = 0;

        AssertRoundTrip(rgba, 64, 48);
    }

    [Theory]
    [InlineData(0x2040_60FFu)]
    [InlineData(0x0000_0000u)]
    public void EncodeLossless_OfOneColour_DecodesToIt(uint colour)
    {
        var rgba = new byte[300 * 200 * 4];
        for (var i = 0; i < rgba.Length; i += 4)
            BitConverter.TryWriteBytes(rgba.AsSpan(i), colour);

        AssertRoundTrip(rgba, 300, 200);
    }

    [Fact]
    public void EncodeLossless_OfTwoColours_DecodesToThem()
    {
        var rgba = new byte[90 * 70 * 4];
        for (var p = 0; p < 90 * 70; p++)
            rgba[p * 4 + (p % 7 == 0 ? 0 : 2)] = 255;
        for (var i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;

        AssertRoundTrip(rgba, 90, 70);
    }

    public static TheoryData<string> Photographs()
    {
        var files = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(WebPFiles.Directory, "lossless-*.webp").Order(StringComparer.Ordinal))
            files.Add(Path.GetFileName(path));

        return files;
    }

    private static void AssertRoundTrip(byte[] rgba, int width, int height)
    {
        using var image = new PixelBuffer(width, height);
        rgba.CopyTo(image.Pixels);
        using var output = new MemoryStream();

        WebPEncoder.EncodeLossless(image, output);

        var encoded = output.ToArray();
        var (libwebp, decodedWidth, decodedHeight) = SkiaOracle.DecodeRgba(encoded);
        Assert.Equal((width, height), (decodedWidth, decodedHeight));
        Assert.True(libwebp.AsSpan().SequenceEqual(rgba), "libwebp does not read back the pixels encoded");
        using var managed = WebPDecoder.Decode(encoded);
        Assert.True(managed.Pixels.SequenceEqual(rgba), "the managed decoder does not read back the pixels encoded");
    }
}
