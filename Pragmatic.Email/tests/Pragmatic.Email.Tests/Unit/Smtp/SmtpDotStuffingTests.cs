using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Smtp;

namespace Pragmatic.Email.Tests.Unit.Smtp;

public sealed class SmtpDotStuffingTests
{
    [Fact]
    public void Apply_LineStartingWithDot_PrefixesSecondDot()
    {
        SmtpDotStuffing.Apply("Line 1\r\n.Line 2").Should().Be("Line 1\r\n..Line 2");
    }

    [Fact]
    public void Apply_PayloadStartingWithDot_PrefixesSecondDot()
    {
        // Regression: a payload whose FIRST line starts with '.' has no preceding CRLF. Leaving it
        // unstuffed produces a lone "." line that terminates DATA early, after which the rest of the
        // body is interpreted as SMTP commands on the authenticated session.
        SmtpDotStuffing.Apply(".\r\nMAIL FROM:<attacker@evil.com>")
            .Should().Be("..\r\nMAIL FROM:<attacker@evil.com>");
    }

    [Fact]
    public void Apply_PayloadThatIsOnlyADot_IsStuffed()
    {
        SmtpDotStuffing.Apply(".").Should().Be("..");
    }

    [Fact]
    public void Apply_BareLineFeedBeforeDot_IsNormalisedAndStuffed()
    {
        // A bare LF is not matched by a naive "\r\n." scan, so the dot line would slip through.
        SmtpDotStuffing.Apply("Line 1\n.Line 2").Should().Be("Line 1\r\n..Line 2");
    }

    [Fact]
    public void Apply_BareCarriageReturnBeforeDot_IsNormalisedAndStuffed()
    {
        SmtpDotStuffing.Apply("Line 1\r.Line 2").Should().Be("Line 1\r\n..Line 2");
    }

    [Fact]
    public void Apply_MultipleDotLines_AllStuffed()
    {
        SmtpDotStuffing.Apply(".a\r\n.b\r\n.c").Should().Be("..a\r\n..b\r\n..c");
    }

    [Fact]
    public void Apply_DotNotAtLineStart_LeftUntouched()
    {
        SmtpDotStuffing.Apply("version 1.0\r\nfine.").Should().Be("version 1.0\r\nfine.");
    }

    [Fact]
    public void Apply_EmptyPayload_ReturnsEmpty()
    {
        SmtpDotStuffing.Apply(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Apply_NoDotsAndCanonicalLineEndings_ReturnsInputUnchanged()
    {
        const string payload = "Subject line\r\n\r\nBody text\r\n";
        SmtpDotStuffing.Apply(payload).Should().Be(payload);
    }

    [Fact]
    public void Apply_MimeMessageWithLeadingDotBody_ProducesNoEarlyTerminator()
    {
        // End-to-end shape check: the wire payload must contain no lone "." line before the
        // terminator the transport appends.
        var message = new EmailMessage
        {
            From = new EmailAddress("sender@example.com"),
            To = [new EmailAddress("recipient@example.com")],
            Subject = "Test",
            TextBody = ".\r\nMAIL FROM:<attacker@evil.com>",
        };

        var wire = SmtpDotStuffing.Apply(Email.Mime.MimeWriter.Write(message)) + "\r\n.";
        var lines = wire.Split("\r\n");

        Array.IndexOf(lines, ".").Should().Be(lines.Length - 1,
            "the only lone '.' line must be the DATA terminator itself");
    }
}
