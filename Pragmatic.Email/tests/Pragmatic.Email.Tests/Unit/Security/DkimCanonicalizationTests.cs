using System.Security.Cryptography;
using System.Text;
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

    // The body is hashed as it is canonicalized and never exists as a string, so each case states the
    // canonical body it expects and compares hashes: equal SHA-256 means the same bytes.

    [Fact]
    public void HashBodyRelaxed_StripsTrailingWhitespace()
    {
        var result = DkimCanonicalization.HashBodyRelaxed("Hello   \r\nWorld  \r\n");

        result.Should().Equal(Sha256("Hello\r\nWorld\r\n"));
    }

    [Fact]
    public void HashBodyRelaxed_CompressesWhitespace()
    {
        var result = DkimCanonicalization.HashBodyRelaxed("Hello   World\r\n");

        result.Should().Equal(Sha256("Hello World\r\n"));
    }

    [Fact]
    public void HashBodyRelaxed_RemovesTrailingEmptyLines()
    {
        var result = DkimCanonicalization.HashBodyRelaxed("Hello\r\n\r\n\r\n");

        result.Should().Equal(Sha256("Hello\r\n"));
    }

    [Fact]
    public void HashBodyRelaxed_KeepsEmptyLinesBetweenLines()
    {
        var result = DkimCanonicalization.HashBodyRelaxed("Hello\r\n\r\n\r\nWorld");

        result.Should().Equal(Sha256("Hello\r\n\r\n\r\nWorld\r\n"));
    }

    [Fact]
    public void HashBodyRelaxed_EndsTheLastLineWithCrlf()
    {
        var result = DkimCanonicalization.HashBodyRelaxed("Hello");

        result.Should().Equal(Sha256("Hello\r\n"));
    }

    [Fact]
    public void HashBodyRelaxed_EmptyBody_HashesOneCrlf()
    {
        var result = DkimCanonicalization.HashBodyRelaxed("");

        result.Should().Equal(Sha256("\r\n"));
    }

    [Fact]
    public void HashBodyRelaxed_LineLongerThanTheFirstBuffer_IsHashedWhole()
    {
        var line = string.Concat(Enumerable.Repeat("word  ", 2000));

        var result = DkimCanonicalization.HashBodyRelaxed(line + "\r\n");

        result.Should().Equal(Sha256(string.Concat(Enumerable.Repeat("word ", 2000)).TrimEnd() + "\r\n"));
    }

    [Fact]
    public void HashBodyRelaxed_BareLineFeed_IsNotALineEnd()
    {
        // RFC 6376 §3.4.4 strips whitespace before CRLF. A bare LF is neither, so the space before
        // it stays (the earlier regex, `[ \t]+$`, also matched before a final "\n" and removed it).
        var result = DkimCanonicalization.HashBodyRelaxed("a \nb\r\n");

        result.Should().Equal(Sha256("a \nb\r\n"));
    }

    private static byte[] Sha256(string canonicalBody) => SHA256.HashData(Encoding.UTF8.GetBytes(canonicalBody));
}
