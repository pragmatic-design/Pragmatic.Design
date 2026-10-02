using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Email.Configuration;
using Pragmatic.Email.Middleware;
using Pragmatic.Email.Mime;
using Pragmatic.Email.Security;

namespace Pragmatic.Email.Tests.Unit.Security;

/// <summary>
///     DKIM and S/MIME applied together, in pipeline order.
/// </summary>
public sealed class SigningPipelineTests
{
    private static EmailMessage CreateMessage() => new()
    {
        From = new EmailAddress("sender@example.com", "Sender"),
        To = [new EmailAddress("recipient@example.com")],
        Subject = "Both signatures",
        TextBody = "Signed twice",
    };

    private static X509Certificate2 CreateCert()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Pragmatic Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    [Fact]
    public async Task Pipeline_SmimeThenDkim_ProducesAValidDkimSignatureOverTheSignedMessage()
    {
        // The ordering matters: S/MIME (Order 50) rewrites the body into a multipart/signed, DKIM
        // (Order 100) then signs the resulting message. With the previous ordering DKIM signed first
        // and S/MIME changed the body underneath it, invalidating what DKIM had just covered.
        using var cert = CreateCert();
        using var dkimKey = RSA.Create(2048);

        var pipeline = new EmailPipeline(
        [
            new SmimeMiddleware(Options.Create(new SmimeOptions { SigningCertificate = cert })),
            new DkimMiddleware(Options.Create(new DkimOptions
            {
                Domain = "example.com",
                Selector = "test",
                PrivateKeyPem = dkimKey.ExportRSAPrivateKeyPem(),
            })),
        ]);

        var processed = await pipeline.ExecuteAsync(CreateMessage(), CancellationToken.None);
        var mime = MimeWriter.Write(processed);

        mime.Should().Contain("Content-Type: multipart/signed");
        mime.Should().Contain("DKIM-Signature:");

        DkimVerifier.Verify(mime, dkimKey).IsValid.Should().BeTrue(
            "DKIM must cover the final, S/MIME-wrapped message");
    }

    [Fact]
    public async Task Pipeline_OrdersSmimeBeforeDkim()
    {
        using var cert = CreateCert();
        using var dkimKey = RSA.Create(2048);

        var smime = new SmimeMiddleware(Options.Create(new SmimeOptions { SigningCertificate = cert }));
        var dkim = new DkimMiddleware(Options.Create(new DkimOptions
        {
            Domain = "example.com",
            Selector = "test",
            PrivateKeyPem = dkimKey.ExportRSAPrivateKeyPem(),
        }));

        smime.Order.Should().BeLessThan(dkim.Order);

        // Registration order must not matter — the pipeline sorts by Order.
        var pipeline = new EmailPipeline([dkim, smime]);
        var processed = await pipeline.ExecuteAsync(CreateMessage(), CancellationToken.None);

        DkimVerifier.Verify(MimeWriter.Write(processed), dkimKey).IsValid.Should().BeTrue();
    }
}
