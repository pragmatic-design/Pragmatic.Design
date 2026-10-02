using Pragmatic.Testing.Assertions;
using Pragmatic.Endpoints.Responses;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     The wire format of the out-of-band file metadata. Both halves of a remote hop go through this
///     one type, so a round-trip here is the whole contract.
/// </summary>
public class PragmaticFileHeadersTests
{
    [Theory]
    [InlineData("invoice.pdf")]
    [InlineData("rapporto annuale 2026.pdf")]
    [InlineData("fattura-€-café.pdf")]
    [InlineData("отчёт.pdf")]
    [InlineData("a;b,c\"d.pdf")]
    public void FileName_SurvivesTheRoundTrip(string fileName)
    {
        var decoded = PragmaticFileHeaders.DecodeFileName(PragmaticFileHeaders.EncodeFileName(fileName));

        decoded.Should().Be(fileName);
    }

    [Fact]
    public void EncodeFileName_StripsTheCharactersThatCouldSplitTheResponse()
    {
        // A raw CR/LF in a header value is a response-splitting primitive. Percent-encoding removes it
        // as a side effect of being a correct encoding, but the property is worth pinning.
        var encoded = PragmaticFileHeaders.EncodeFileName("evil\r\nX-Injected: 1.pdf");

        encoded.Should().NotContain("\r").And.NotContain("\n");
        PragmaticFileHeaders.DecodeFileName(encoded).Should().Be("evil\r\nX-Injected: 1.pdf");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DecodeFileName_NothingUsable_ReturnsNull(string? value)
        => PragmaticFileHeaders.DecodeFileName(value).Should().BeNull();

    [Fact]
    public void DecodeFileName_MalformedEscape_CostsTheNameNotTheDownload()
    {
        // A broken peer must never make the download fail: an unusable escape sequence is passed
        // through as-is rather than throwing.
        PragmaticFileHeaders.DecodeFileName("%ZZ").Should().Be("%ZZ");
    }

    [Fact]
    public void Inline_SurvivesTheRoundTrip()
    {
        PragmaticFileHeaders.DecodeInline(PragmaticFileHeaders.EncodeInline(true)).Should().BeTrue();
        PragmaticFileHeaders.DecodeInline(PragmaticFileHeaders.EncodeInline(false)).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("1")]
    public void DecodeInline_AnythingButTrue_IsAttachment(string? value)
        => PragmaticFileHeaders.DecodeInline(value).Should().BeFalse();
}
