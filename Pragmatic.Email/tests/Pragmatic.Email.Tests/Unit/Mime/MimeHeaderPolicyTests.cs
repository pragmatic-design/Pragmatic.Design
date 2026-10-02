using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Mime;

namespace Pragmatic.Email.Tests.Unit.Mime;

/// <summary>
///     Rules governing which headers a caller may set, and how the Date header is emitted.
/// </summary>
public sealed class MimeHeaderPolicyTests
{
    private static EmailMessage Message(IReadOnlyDictionary<string, string>? headers = null) => new()
    {
        From = new EmailAddress("sender@example.com"),
        To = [new EmailAddress("recipient@example.com")],
        Subject = "Test",
        TextBody = "Body",
        Headers = headers ?? new Dictionary<string, string>(),
    };

    [Theory]
    [InlineData("Bcc")]
    [InlineData("bcc")]
    [InlineData("To")]
    [InlineData("From")]
    [InlineData("Content-Type")]
    [InlineData("Message-ID")]
    public void Write_ReservedHeader_IsRejected(string headerName)
    {
        // "Bcc" would add a hidden recipient that never appears in the message model and never reaches
        // RCPT TO; a second Content-Type would break the MIME structure outright.
        var message = Message(new Dictionary<string, string> { [headerName] = "value@example.com" });

        var act = () => MimeWriter.Write(message);

        act.Should().Throw<ArgumentException>().WithMessage("*controlled by the message itself*");
    }

    [Fact]
    public void Write_CustomHeader_IsAllowed()
    {
        var message = Message(new Dictionary<string, string> { ["X-Campaign-Id"] = "spring-2026" });

        MimeWriter.Write(message).Should().Contain("X-Campaign-Id: spring-2026");
    }

    [Fact]
    public void Write_DkimSignatureHeader_IsAllowed()
    {
        // The DKIM middleware adds its signature through Headers — the reserved list must not block it.
        var message = Message(new Dictionary<string, string> { ["DKIM-Signature"] = "v=1; a=rsa-sha256;" });

        MimeWriter.Write(message).Should().Contain("DKIM-Signature: v=1;");
    }

    [Fact]
    public void Write_DateWithOffset_IsConvertedToUtc()
    {
        // "R" writes "GMT" without converting, so an unconverted non-zero offset is transmitted as the
        // wrong instant — an hour or more off, which mail clients display verbatim.
        var instant = new DateTimeOffset(2026, 7, 25, 14, 30, 0, TimeSpan.FromHours(2));
        var message = Message() with { Date = instant };

        var mime = MimeWriter.Write(message);

        mime.Should().Contain("Date: Sat, 25 Jul 2026 12:30:00 GMT");
    }
}
