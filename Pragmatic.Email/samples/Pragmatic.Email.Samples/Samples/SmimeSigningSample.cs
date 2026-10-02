using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Email.Builder;
using Pragmatic.Email.Extensions;
using Pragmatic.Email.Security;
using Pragmatic.Email.Testing;

namespace Pragmatic.Email.Samples.Samples;

/// <summary>
///     S/MIME signing two ways: (1) DI wiring via <c>EmailBuilder.EnableSmime</c>, which adds the
///     internal <c>SmimeMiddleware</c> so each send carries an <c>X-Pragmatic-Smime-Signature</c>
///     header; and (2) the public <see cref="SmimeSigner"/> producing a detached PKCS#7 signature
///     directly. A self-signed certificate with a private key is generated in-process.
/// </summary>
public static class SmimeSigningSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- SmimeSigning (EnableSmime) ---");

        using var certificate = CreateSelfSignedCertificate();

        var services = new ServiceCollection();
        services.AddLogging();

        // EnableSmime registers a SmimeMiddleware (Order 200) that signs inside the pipeline.
        services.AddPragmaticEmail(email => email
            .UseNullTransport()
            .EnableSmime(o =>
            {
                o.SigningCertificate = certificate;
                o.IncludeCertificate = true;
            }));

        var harness = services.AddEmailTestHarness();
        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IEmailSender>();

        var message = new EmailMessageBuilder()
            .From("legal@example.com")
            .To("counterparty@example.com")
            .Subject("Signed agreement")
            .TextBody("This message passed through the S/MIME middleware.")
            .Build();

        await sender.SendAsync(message);

        var sent = harness.Sent[0].Message;
        var present = sent.Headers.ContainsKey("X-Pragmatic-Smime-Signature");
        Console.WriteLine($"  Certificate subject                       : {certificate.Subject}");
        Console.WriteLine($"  Pipeline added X-Pragmatic-Smime-Signature: {present}");

        // Direct use of the public signer over an arbitrary MIME content string.
        var signer = new SmimeSigner(new SmimeOptions { SigningCertificate = certificate });
        var mimeContent = $"Subject: {message.Subject}\r\n\r\n{message.TextBody}\r\n";
        var pkcs7 = signer.Sign(mimeContent);
        Console.WriteLine($"  Direct SmimeSigner.Sign(...) PKCS#7 bytes : {pkcs7.Length}");
        Console.WriteLine();
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Pragmatic.Email Sample Signer",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));
    }
}
