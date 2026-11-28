using Pragmatic.Imaging.Codecs.Ccitt;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Visual;

namespace Pragmatic.Imaging.Codecs.Tests.Ccitt;

public sealed class CcittDecoderTests
{
    private static readonly string Files = Path.Combine(AppContext.BaseDirectory, "References", "Ccitt");

    [Fact]
    public void TheFiles_AreEachListedWithTheirProvenance() => ReferenceManifest.AssertComplete(Files);

    public static TheoryData<string> Streams() => [.. Directory.EnumerateFiles(Files, "*.fax").Select(p => Path.GetFileNameWithoutExtension(p)).Order()];

    [Theory]
    [MemberData(nameof(Streams))]
    public void ReadLine_OfAStreamLibtiffCoded_GivesTheImageItCoded(string name)
    {
        var size = name[(name.LastIndexOf('-') + 1)..].Split('x');
        int width = int.Parse(size[0]), height = int.Parse(size[1]);
        var k = name.Contains("-g4-") ? -1 : name.Contains("-2d") ? 1 : 0;
        var expected = File.ReadAllBytes(Path.Combine(Files, name + ".bits"));
        var decoder = new CcittDecoder(File.ReadAllBytes(Path.Combine(Files, name + ".fax")), width, k);
        var stride = (width + 7) / 8;
        var row = new byte[stride];

        for (var y = 0; y < height; y++)
        {
            decoder.ReadLine(row).Should().BeTrue($"{name}: line {y} is there");
            row.Should().Equal(expected.AsSpan(y * stride, stride).ToArray(), $"{name}: line {y}");
        }
    }

    [Fact]
    public void ReadLine_PastAGroup4EndOfBlock_IsTheEnd()
    {
        // Two lines all white (vertical 0 against the imaginary white line, twice), then the end of block: two EOLs.
        // 1 1 | 0000 0000 0001 | 0000 0000 0001
        byte[] data = [0b1100_0000, 0b0000_0100, 0b0000_0000, 0b0100_0000];
        var decoder = new CcittDecoder(data, 8);
        var row = new byte[1];

        decoder.ReadLine(row).Should().BeTrue();
        row[0].Should().Be(0);
        decoder.ReadLine(row).Should().BeTrue();
        decoder.ReadLine(row).Should().BeFalse();
    }

    [Fact]
    public void ReadLine_OfAnUnknownCode_Throws()
    {
        // Seven zeros and a one is the extension mode; then nothing a line can be.
        var decoder = new CcittDecoder(new byte[] { 0b0000_0010, 0 }, 16);

        Assert.Throws<ImageDecodeException>(() => decoder.ReadLine(new byte[2]));
    }

    [Fact]
    public void ReadLine_OfGroup3ByteAligned_StartsEachLineOnAByte()
    {
        // One-dimensional, 8 pixels: white 3 (1000) black 5 (0011), padded to the byte; then white 8 (10011).
        byte[] data = [0b1000_0011, 0b1001_1000];
        var decoder = new CcittDecoder(data, 8, k: 0, byteAligned: true);
        var row = new byte[1];

        decoder.ReadLine(row).Should().BeTrue();
        row[0].Should().Be(0b0001_1111);
        decoder.ReadLine(row).Should().BeTrue();
        row[0].Should().Be(0);
    }
}
