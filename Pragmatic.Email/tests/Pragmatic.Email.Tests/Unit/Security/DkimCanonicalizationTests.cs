using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Security;

namespace Pragmatic.Email.Tests.Unit.Security;

public sealed class DkimCanonicalizationTests
{
    [Fact]
    public void CanonicalizeHeaderRelaxed_LowercasesName()
    {
        var result = DkimCanonicalization.CanonicalizeHeaderRelaxed("Subject", "Test");

        result.Should().StartWith("subject:");
    }

    [Fact]
    public void CanonicalizeHeaderRelaxed_UnfoldsAndCompresses()
    {
        var result = DkimCanonicalization.CanonicalizeHeaderRelaxed(
            "Subject", "Hello\r\n   World  Test");

        result.Should().Be("subject:Hello World Test");
    }

    [Fact]
    public void CanonicalizeHeaderRelaxed_TrimsValue()
    {
        var result = DkimCanonicalization.CanonicalizeHeaderRelaxed(
            "From", "  sender@example.com  ");

        result.Should().Be("from:sender@example.com");
    }

    [Fact]
    public void CanonicalizeBodyRelaxed_StripsTrailingWhitespace()
    {
        var result = DkimCanonicalization.CanonicalizeBodyRelaxed("Hello   \r\nWorld  \r\n");

        result.Should().Be("Hello\r\nWorld\r\n");
    }

    [Fact]
    public void CanonicalizeBodyRelaxed_CompressesWhitespace()
    {
        var result = DkimCanonicalization.CanonicalizeBodyRelaxed("Hello   World\r\n");

        result.Should().Be("Hello World\r\n");
    }

    [Fact]
    public void CanonicalizeBodyRelaxed_RemovesTrailingEmptyLines()
    {
        var result = DkimCanonicalization.CanonicalizeBodyRelaxed("Hello\r\n\r\n\r\n");

        result.Should().Be("Hello\r\n");
    }

    [Fact]
    public void CanonicalizeBodyRelaxed_KeepsFinalCrlf()
    {
        var result = DkimCanonicalization.CanonicalizeBodyRelaxed("Hello\r\n");

        result.Should().EndWith("\r\n");
    }
}
