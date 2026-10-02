using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Email.Mime;
using Pragmatic.Email.Security;

namespace Pragmatic.Email.Tests.Unit.Security;

public sealed class SmimeMiddlewareTests
{
    private static X509Certificate2 CreateCert()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Pragmatic Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static SmimeMiddleware CreateMiddleware(X509Certificate2 cert)
        => new(Options.Create(new SmimeOptions { SigningCertificate = cert }));

    private static EmailMessage CreateMessage() => new()
    {
        From = new EmailAddress("sender@example.com"),
        To = [new EmailAddress("recipient@example.com")],
        Subject = "S/MIME Test",
        TextBody = "Body",
    };

    private static Task<EmailMessage> Identity(EmailMessage m) => Task.FromResult(m);

    [Fact]
    public void Order_RunsBeforeDkim()
    {
        using var cert = CreateCert();

        // S/MIME rewrites the body into a multipart/signed, so it must run BEFORE DkimMiddleware
        // (Order = 100): DKIM has to sign the final message, otherwise its signature covers a body
        // that no longer exists.
        CreateMiddleware(cert).Order.Should().Be(50);
        CreateMiddleware(cert).Order.Should().BeLessThan(100);
    }

    [Fact]
    public async Task ProcessAsync_ProducesMultipartSignedPerRfc8551()
    {
        using var cert = CreateCert();

        var result = await CreateMiddleware(cert).ProcessAsync(CreateMessage(), Identity, CancellationToken.None);
        var mime = MimeWriter.Write(result);

        mime.Should().Contain("Content-Type: multipart/signed");
        mime.Should().Contain("protocol=\"application/pkcs7-signature\"");
        mime.Should().Contain("micalg=sha-256");
        mime.Should().Contain("Content-Type: application/pkcs7-signature; name=\"smime.p7s\"");
        mime.Should().Contain("Content-Disposition: attachment; filename=\"smime.p7s\"");
    }

    [Fact]
    public async Task ProcessAsync_NoLongerUsesTheCustomHeader()
    {
        // A signature parked in X-Pragmatic-Smime-Signature is one no mail client recognises — the
        // message looks signed to the sender and unsigned to every recipient.
        using var cert = CreateCert();

        var result = await CreateMiddleware(cert).ProcessAsync(CreateMessage(), Identity, CancellationToken.None);

        result.Headers.Should().NotContainKey("X-Pragmatic-Smime-Signature");
    }

    [Fact]
    public async Task ProcessAsync_SignatureVerifiesAgainstTheEmbeddedContent()
    {
        using var cert = CreateCert();

        var result = await CreateMiddleware(cert).ProcessAsync(CreateMessage(), Identity, CancellationToken.None);
        var mime = MimeWriter.Write(result);

        var (content, signature) = ExtractSignedParts(mime);

        var cms = new SignedCms(new ContentInfo(Encoding.UTF8.GetBytes(content)), detached: true);
        cms.Decode(signature);

        var verify = () => cms.CheckSignature(verifySignatureOnly: true);
        verify.Should().NotThrow("the detached signature must cover the embedded content part exactly");
    }

    [Fact]
    public async Task ProcessAsync_TamperedContent_FailsVerification()
    {
        using var cert = CreateCert();

        var result = await CreateMiddleware(cert).ProcessAsync(CreateMessage(), Identity, CancellationToken.None);
        var mime = MimeWriter.Write(result);

        var (content, signature) = ExtractSignedParts(mime);
        var tampered = content.Replace("Body", "Tampered", StringComparison.Ordinal);

        var cms = new SignedCms(new ContentInfo(Encoding.UTF8.GetBytes(tampered)), detached: true);
        cms.Decode(signature);

        var verify = () => cms.CheckSignature(verifySignatureOnly: true);
        verify.Should().Throw<CryptographicException>();
    }

    [Fact]
    public async Task ProcessAsync_OriginalContentRemainsReadable()
    {
        // A multipart/signed keeps the original content in the clear: a client without S/MIME support
        // must still be able to read the message.
        using var cert = CreateCert();

        var result = await CreateMiddleware(cert).ProcessAsync(CreateMessage(), Identity, CancellationToken.None);
        var mime = MimeWriter.Write(result);

        mime.Should().Contain("Content-Type: text/plain; charset=utf-8");
        mime.Should().Contain("Body");
    }

    [Fact]
    public async Task ProcessAsync_DoesNotMutateInputMessage()
    {
        using var cert = CreateCert();
        var message = CreateMessage();

        await CreateMiddleware(cert).ProcessAsync(message, Identity, CancellationToken.None);

        MimeWriter.Write(message).Should().NotContain("multipart/signed");
    }

    [Fact]
    public async Task ProcessAsync_RenderIsStillDeterministic()
    {
        using var cert = CreateCert();

        var result = await CreateMiddleware(cert).ProcessAsync(CreateMessage(), Identity, CancellationToken.None);

        MimeWriter.Write(result).Should().Be(MimeWriter.Write(result));
    }

    /// <summary>
    ///     Pulls the signed content part and the DER signature out of a multipart/signed message,
    ///     the way a receiving client would.
    /// </summary>
    private static (string Content, byte[] Signature) ExtractSignedParts(string mime)
    {
        var boundary = "--" + mime.Split("boundary=\"")[1].Split('"')[0];

        var sections = mime.Split(boundary, StringSplitOptions.None);
        // sections[0] = message headers, [1] = content part, [2] = signature part, [3] = trailing "--"
        var contentSection = sections[1];
        var signatureSection = sections[2];

        // Each section starts with the CRLF that terminated the boundary line, and ends with the CRLF
        // that precedes the next one; the signed content is what lies between them.
        var content = contentSection[2..^2];

        var signatureBody = signatureSection[(signatureSection.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..];
        var signature = Convert.FromBase64String(
            signatureBody.Replace("\r\n", string.Empty, StringComparison.Ordinal).Trim());

        return (content, signature);
    }
}
