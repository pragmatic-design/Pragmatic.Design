using System.Security.Cryptography;
using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Configuration;
using Pragmatic.Email.Mime;
using Pragmatic.Email.Security;

namespace Pragmatic.Email.Tests.Unit.Security;

public sealed class DkimSignerTests
{
    private static (DkimSigner Signer, RSA Key) CreateSigner()
    {
        var rsa = RSA.Create(2048);
        var signer = new DkimSigner(new DkimOptions
        {
            Domain = "example.com",
            Selector = "test",
            PrivateKeyPem = rsa.ExportRSAPrivateKeyPem(),
        });

        return (signer, rsa);
    }

    private static EmailMessage CreateMessage(
        string subject = "DKIM Test",
        string? textBody = "Hello DKIM",
        string? htmlBody = null,
        IReadOnlyList<EmailAttachment>? attachments = null) => new()
    {
        From = new EmailAddress("sender@example.com", "Sender"),
        To = [new EmailAddress("recipient@example.com")],
        Subject = subject,
        TextBody = textBody,
        HtmlBody = htmlBody,
        Attachments = attachments ?? [],
    };

    /// <summary>Renders the message the way the transport does, with the signature header applied.</summary>
    private static string SignAndRender(DkimSigner signer, EmailMessage message)
    {
        var signature = signer.Sign(MimeWriter.Write(message));
        var signed = message with
        {
            Headers = new Dictionary<string, string>(message.Headers) { ["DKIM-Signature"] = signature },
        };

        return MimeWriter.Write(signed);
    }

    [Fact]
    public void Sign_PlainTextMessage_ProducesAVerifiableSignature()
    {
        var (signer, key) = CreateSigner();

        var result = DkimVerifier.Verify(SignAndRender(signer, CreateMessage()), key);

        result.BodyHashMatches.Should().BeTrue("bh= must cover the body only");
        result.SignatureValid.Should().BeTrue();
    }

    [Fact]
    public void Sign_MultipartMessage_ProducesAVerifiableSignature()
    {
        // MIME boundaries regenerated per render would make the signed document and the transmitted
        // one differ, and no multipart signature could ever verify.
        var (signer, key) = CreateSigner();
        var message = CreateMessage(htmlBody: "<b>Hello DKIM</b>");

        DkimVerifier.Verify(SignAndRender(signer, message), key).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Sign_MessageWithAttachment_ProducesAVerifiableSignature()
    {
        var (signer, key) = CreateSigner();
        var message = CreateMessage(attachments:
        [
            new EmailAttachment
            {
                FileName = "invoice.pdf",
                ContentType = "application/pdf",
                Data = new byte[] { 1, 2, 3, 4, 5 },
            },
        ]);

        DkimVerifier.Verify(SignAndRender(signer, message), key).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Sign_NonAsciiSubject_ProducesAVerifiableSignature()
    {
        // Signing message.Subject raw while the MIME carries the RFC 2047 encoded form would break
        // the signature on any accented subject — the norm in Italian.
        var (signer, key) = CreateSigner();

        DkimVerifier.Verify(SignAndRender(signer, CreateMessage(subject: "Città — prenotazione")), key)
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Sign_NonAsciiDisplayName_ProducesAVerifiableSignature()
    {
        var (signer, key) = CreateSigner();
        var message = CreateMessage() with { From = new EmailAddress("sender@example.com", "Müller Gmbh") };

        DkimVerifier.Verify(SignAndRender(signer, message), key).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Sign_TamperedBody_FailsVerification()
    {
        // Guards against the verifier being vacuously true.
        var (signer, key) = CreateSigner();
        var rendered = SignAndRender(signer, CreateMessage());

        var tampered = rendered.Replace("Hello DKIM", "Goodbye DKIM", StringComparison.Ordinal);

        DkimVerifier.Verify(tampered, key).BodyHashMatches.Should().BeFalse();
    }

    [Fact]
    public void Sign_TamperedSubject_FailsVerification()
    {
        var (signer, key) = CreateSigner();
        var rendered = SignAndRender(signer, CreateMessage());

        var tampered = rendered.Replace("Subject: DKIM Test", "Subject: Injected", StringComparison.Ordinal);

        DkimVerifier.Verify(tampered, key).SignatureValid.Should().BeFalse();
    }

    [Fact]
    public void Sign_SignatureFromADifferentKey_FailsVerification()
    {
        var (signer, _) = CreateSigner();
        using var otherKey = RSA.Create(2048);

        DkimVerifier.Verify(SignAndRender(signer, CreateMessage()), otherKey)
            .SignatureValid.Should().BeFalse();
    }

    [Fact]
    public void Sign_DeclaresOnlyHeadersThatArePresent()
    {
        var (signer, _) = CreateSigner();

        var signature = signer.Sign(MimeWriter.Write(CreateMessage()));

        var h = signature.Split("h=")[1].Split(';')[0].Trim();
        h.Split(':').Should().Contain(["from", "to", "subject", "date", "message-id"]);
        h.Split(':').Should().NotContain("cc", "the message has no Cc header, so h= must not name one");
    }

    [Fact]
    public void Sign_ProducesRequiredTags()
    {
        var (signer, _) = CreateSigner();

        var signature = signer.Sign(MimeWriter.Write(CreateMessage()));

        signature.Should().Contain("v=1");
        signature.Should().Contain("a=rsa-sha256");
        signature.Should().Contain("c=relaxed/relaxed");
        signature.Should().Contain("d=example.com");
        signature.Should().Contain("s=test");
        signature.Should().Contain("t=");
        signature.Should().Contain("bh=");
    }

    [Fact]
    public void SplitHeadersAndBody_UnfoldsContinuationLines()
    {
        const string mime = "Subject: first\r\n continued\r\nFrom: a@b.com\r\n\r\nbody";

        var (headers, body) = DkimSigner.SplitHeadersAndBody(mime);

        body.Should().Be("body");
        headers.Should().HaveCount(2);
        headers[0].Value.Should().Be("first\r\n continued");
    }
}
