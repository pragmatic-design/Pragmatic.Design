using Pragmatic.Imaging.Codecs.WebP;

namespace Pragmatic.Imaging.Codecs.Tests.WebP;

/// <summary>
///     A WebP that is not right, truncated anywhere or with bytes changed, is refused with
///     <see cref="ImageDecodeException" /> or decoded to something: never another exception, never a hang.
/// </summary>
public sealed class WebPDecoderRobustnessTests
{
    [Theory]
    [InlineData("lossy-31x13.webp")]
    [InlineData("lossless-31x13.webp")]
    [InlineData("lossy-alpha-aq40.webp")]
    [InlineData("lossless-palette-4.webp")]
    [InlineData("animated-translucent.webp")]
    public void Decode_OfEveryTruncation_RefusesOrDecodes(string file)
    {
        var encoded = WebPFiles.Read(file);
        for (var length = 0; length < encoded.Length; length++)
            DecodeOrRefuse(encoded.AsSpan(0, length).ToArray(), $"{file} cut at {length}");
    }

    [Theory]
    [InlineData("lossy-31x13.webp")]
    [InlineData("lossless-31x13.webp")]
    [InlineData("lossy-alpha-aq40.webp")]
    [InlineData("lossless-palette-16.webp")]
    [InlineData("animated-alpha.webp")]
    public void Decode_OfChangedBytes_RefusesOrDecodes(string file)
    {
        var original = WebPFiles.Read(file);
        var random = new Random(853);
        for (var trial = 0; trial < 300; trial++)
        {
            var changed = original.ToArray();
            for (var i = 0; i < 1 + trial % 4; i++)
                changed[random.Next(changed.Length)] = (byte)random.Next(256);
            DecodeOrRefuse(changed, $"{file} trial {trial}");
        }
    }

    private static void DecodeOrRefuse(byte[] encoded, string what)
    {
        try
        {
            WebPDecoder.Identify(encoded);
            using var decoded = WebPDecoder.Decode(encoded);
        }
        catch (ImageDecodeException)
        {
            // Refused: what the test allows.
        }
        catch (Exception e)
        {
            Assert.Fail($"{what}: {e.GetType().Name}: {e.Message}");
        }
    }
}
