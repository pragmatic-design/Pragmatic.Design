using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Mime;

namespace Pragmatic.Email.Tests.Unit.Mime;

public sealed class MimeWriterTests
{
    private static EmailMessage CreateMessage(
        string? textBody = "Hello",
        string? htmlBody = null,
        IReadOnlyList<EmailAttachment>? attachments = null) => new()
    {
        From = new EmailAddress("sender@example.com", "Sender"),
        To = [new EmailAddress("recipient@example.com")],
        Subject = "Test Subject",
        TextBody = textBody,
        HtmlBody = htmlBody,
        Attachments = attachments ?? [],
    };

    [Fact]
    public void Write_AddressWithCrlf_DoesNotInjectHeaderLine()
    {
        // C2: an address built via the implicit string→EmailAddress operator bypasses Validated. A CRLF in
        // it must NOT start a new MIME header line (e.g. an injected Bcc).
        EmailAddress malicious = "victim@example.com\r\nBcc: attacker@evil.com";
        var message = new EmailMessage
        {
            From = new EmailAddress("sender@example.com"),
            To = [malicious],
            Subject = "Test",
            TextBody = "Hello",
        };

        var mime = MimeWriter.Write(message);

        mime.Should().NotContain("\nBcc:", "the CRLF must be stripped so it cannot inject a header line");
    }

    [Fact]
    public void Write_PlainTextOnly_NoMultipart()
    {
        var mime = MimeWriter.Write(CreateMessage());

        mime.Should().Contain("Content-Type: text/plain; charset=utf-8");
        mime.Should().Contain("Hello");
        mime.Should().NotContain("multipart");
    }

    [Fact]
    public void Write_TextAndHtml_ProducesMultipartAlternative()
    {
        var mime = MimeWriter.Write(CreateMessage(htmlBody: "<b>Hello</b>"));

        mime.Should().Contain("Content-Type: multipart/alternative");
        mime.Should().Contain("Content-Type: text/plain; charset=utf-8");
        mime.Should().Contain("Content-Type: text/html; charset=utf-8");
        mime.Should().Contain("Hello");
        mime.Should().Contain("<b>Hello</b>");
    }

    [Fact]
    public void Write_WithAttachment_ProducesMultipartMixed()
    {
        var attachment = new EmailAttachment
        {
            FileName = "test.pdf",
            ContentType = "application/pdf",
            Data = new byte[] { 1, 2, 3, 4, 5 },
        };

        var mime = MimeWriter.Write(CreateMessage(attachments: [attachment]));

        mime.Should().Contain("Content-Type: multipart/mixed");
        mime.Should().Contain("Content-Disposition: attachment; filename=\"test.pdf\"");
        mime.Should().Contain("Content-Transfer-Encoding: base64");
    }

    [Fact]
    public void Write_WithInlineImage_ProducesMultipartRelated()
    {
        var inline = new EmailAttachment
        {
            FileName = "logo.png",
            ContentType = "image/png",
            Data = new byte[] { 1, 2, 3 },
            IsInline = true,
            ContentId = "logo",
        };

        var mime = MimeWriter.Write(CreateMessage(htmlBody: "<img src='cid:logo'>", attachments: [inline]));

        mime.Should().Contain("Content-Type: multipart/related");
        mime.Should().Contain("Content-ID: <logo>");
        mime.Should().Contain("Content-Disposition: inline");
    }

    [Fact]
    public void Write_WithAttachmentAndInlineImage_ProducesCorrectNesting()
    {
        var attachment = new EmailAttachment
        {
            FileName = "doc.pdf",
            ContentType = "application/pdf",
            Data = new byte[] { 1, 2, 3 },
        };
        var inline = new EmailAttachment
        {
            FileName = "logo.png",
            ContentType = "image/png",
            Data = new byte[] { 4, 5, 6 },
            IsInline = true,
            ContentId = "logo",
        };

        var mime = MimeWriter.Write(CreateMessage(htmlBody: "<img src='cid:logo'>", attachments: [attachment, inline]));

        mime.Should().Contain("Content-Type: multipart/mixed");
        mime.Should().Contain("Content-Type: multipart/related");
        mime.Should().Contain("Content-ID: <logo>");
        mime.Should().Contain("Content-Disposition: attachment; filename=\"doc.pdf\"");
    }

    [Fact]
    public void Write_Base64Encoding_WrapsAt76Chars()
    {
        var largeData = new byte[200];
        Array.Fill(largeData, (byte)0xFF);
        var attachment = new EmailAttachment
        {
            FileName = "data.bin",
            ContentType = "application/octet-stream",
            Data = largeData,
        };

        var mime = MimeWriter.Write(CreateMessage(attachments: [attachment]));

        var lines = mime.Split("\r\n");
        var base64Lines = lines.Where(l => l.Length > 0 && l.All(c => "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=".Contains(c)));
        base64Lines.Should().AllSatisfy(l => l.Length.Should().BeLessThanOrEqualTo(76));
    }

    [Fact]
    public void Write_UnicodeSubject_EncodesBase64()
    {
        var message = CreateMessage() with { Subject = "Prenotazione confermata ✓" };

        var mime = MimeWriter.Write(message);

        mime.Should().Contain("Subject: =?utf-8?B?");
    }

    [Fact]
    public void Write_Headers_IncludesFromToSubjectDate()
    {
        var mime = MimeWriter.Write(CreateMessage());

        mime.Should().Contain("From: Sender <sender@example.com>");
        mime.Should().Contain("To: recipient@example.com");
        mime.Should().Contain("Subject: Test Subject");
        mime.Should().Contain("MIME-Version: 1.0");
        mime.Should().Contain("Message-ID: <");
    }

    [Fact]
    public void Write_CustomHeaders_Included()
    {
        var message = CreateMessage() with
        {
            Headers = new Dictionary<string, string> { ["X-Custom"] = "value" },
        };

        var mime = MimeWriter.Write(message);

        mime.Should().Contain("X-Custom: value");
    }

    [Fact]
    public void Write_DotStuffing_IsNotAppliedByTheMimeWriter()
    {
        // Dot-stuffing is a transport encoding (RFC 5321 §4.5.2) applied by SmtpDotStuffing over the
        // whole DATA block. Doing it here would corrupt the message that DKIM/S-MIME sign, since a
        // verifier only ever sees the payload after the receiving server removed the stuffing.
        var message = CreateMessage(textBody: "Line 1\r\n.Line with dot\r\nLine 3");

        var mime = MimeWriter.Write(message);

        mime.Should().Contain("\r\n.Line with dot");
        mime.Should().NotContain("\r\n..Line with dot");
    }

    [Fact]
    public void Write_CalledTwice_ProducesIdenticalOutput()
    {
        // Boundaries must not be a fresh GUID per call, or the render a signing middleware hashes and
        // the render the transport sends are different documents.
        var message = CreateMessage(htmlBody: "<b>Hello</b>", attachments:
        [
            new EmailAttachment
            {
                FileName = "a.pdf", ContentType = "application/pdf", Data = new byte[] { 1, 2, 3 },
            },
            new EmailAttachment
            {
                FileName = "logo.png", ContentType = "image/png", Data = new byte[] { 4, 5, 6 },
                IsInline = true, ContentId = "logo",
            },
        ]);

        MimeWriter.Write(message).Should().Be(MimeWriter.Write(message));
    }

    [Fact]
    public void Write_DifferentMessages_UseDifferentBoundaries()
    {
        var first = MimeWriter.Write(CreateMessage(htmlBody: "<b>a</b>"));
        var second = MimeWriter.Write(CreateMessage(htmlBody: "<b>b</b>"));

        ExtractBoundary(first).Should().NotBe(ExtractBoundary(second));

        static string ExtractBoundary(string mime)
            => mime.Split("boundary=\"")[1].Split('"')[0];
    }

    [Fact]
    public void Write_BareLineFeedBody_NormalisedToCrLf()
    {
        var message = CreateMessage(textBody: "Line 1\nLine 2");

        var mime = MimeWriter.Write(message);

        mime.Should().Contain("Line 1\r\nLine 2");
    }

    [Fact]
    public void Write_CcRecipients_Included()
    {
        var message = CreateMessage() with
        {
            Cc = [new EmailAddress("cc@example.com", "CC Person")],
        };

        var mime = MimeWriter.Write(message);

        mime.Should().Contain("Cc: CC Person <cc@example.com>");
    }

    [Fact]
    public void Write_ReplyTo_Included()
    {
        var message = CreateMessage() with
        {
            ReplyTo = new EmailAddress("reply@example.com"),
        };

        var mime = MimeWriter.Write(message);

        mime.Should().Contain("Reply-To: reply@example.com");
    }
}
