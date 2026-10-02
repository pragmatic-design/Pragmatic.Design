using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Email.Builder;
using Pragmatic.Email.Configuration;
using Pragmatic.Email.Extensions;
using Pragmatic.Email.Mime;
using Pragmatic.Email.Security;
using Pragmatic.Email.Testing;

namespace Pragmatic.Email.Samples.Samples;

/// <summary>
///     DKIM signing two ways: (1) DI wiring via <c>EmailBuilder.EnableDkim</c>, which adds the
///     internal <c>DkimMiddleware</c> to the pipeline so every send picks up a <c>DKIM-Signature</c>
///     header; and (2) the public <see cref="DkimSigner"/> producing that header value directly.
///     A throwaway RSA key is generated in-process — never store a real signing key in source.
/// </summary>
public static class DkimSigningSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- DkimSigning (EnableDkim) ---");

        // Generate a throwaway RSA key, exported as PEM (stand-in for a secret-managed key).
        using var rsa = RSA.Create(2048);
        var privateKeyPem = rsa.ExportPkcs8PrivateKeyPem();

        var services = new ServiceCollection();
        services.AddLogging();

        // EnableDkim registers a DkimMiddleware that signs inside the pipeline.
        services.AddPragmaticEmail(email => email
            .UseNullTransport()
            .EnableDkim(o =>
            {
                o.Domain = "example.com";
                o.Selector = "default";
                o.PrivateKeyPem = privateKeyPem;
            }));

        var harness = services.AddEmailTestHarness();
        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IEmailSender>();

        var message = new EmailMessageBuilder()
            .From("newsletter@example.com")
            .To("subscriber@example.com")
            .Subject("Signed newsletter")
            .TextBody("This message passed through the DKIM middleware.")
            .Build();

        await sender.SendAsync(message);

        var sent = harness.Sent[0].Message;
        var pipelineSignature = sent.Headers.TryGetValue("DKIM-Signature", out var sig) ? sig : "(none)";
        Console.WriteLine($"  Pipeline added DKIM-Signature header : {sent.Headers.ContainsKey("DKIM-Signature")}");
        Console.WriteLine($"  Header value (pipeline)              : {Truncate(pipelineSignature, 84)}");

        // Direct use of the public signer. It takes the fully rendered MIME message — headers, blank
        // line, body — and signs it as it will go on the wire: header values are read back verbatim
        // and bh= covers the body only. Hand-building an approximation here would produce a signature
        // that does not match what gets sent.
        using var signer = new DkimSigner(provider.GetRequiredService<IOptions<DkimOptions>>().Value);
        var direct = signer.Sign(MimeWriter.Write(message));
        Console.WriteLine($"  Direct DkimSigner.Sign(...)          : {Truncate(direct, 84)}");
        Console.WriteLine();
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "...";
}
