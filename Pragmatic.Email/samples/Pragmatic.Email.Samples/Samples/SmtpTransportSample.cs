using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Email.Builder;
using Pragmatic.Email.Configuration;
using Pragmatic.Email.Extensions;
using Pragmatic.Email.Transport;

namespace Pragmatic.Email.Samples.Samples;

/// <summary>
///     Configures the pooled SMTP transport via <c>EmailBuilder.UseSmtp</c>: host, port,
///     STARTTLS, authentication method, and the connection-pool knobs
///     (<see cref="SmtpTransportOptions.MaxConnections"/>,
///     <see cref="SmtpTransportOptions.MaxMessagesPerConnection"/>,
///     <see cref="SmtpTransportOptions.IdleTimeoutSeconds"/>).
///     No real server is contacted — the SMTP transport connects lazily on the first
///     <c>SendAsync</c>. Point <c>Host</c>/<c>Port</c> at a real server (or a local catcher
///     such as MailHog) and call <c>IEmailSender.SendAsync</c> to actually deliver.
/// </summary>
public static class SmtpTransportSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- SmtpTransport + connection pooling (UseSmtp) ---");

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPragmaticEmail(email => email.UseSmtp(o =>
        {
            o.Host = "smtp.example.com";
            o.Port = 587;
            o.UseSsl = true;                  // upgrade via STARTTLS
            o.AuthMethod = SmtpAuthMethod.Login;
            o.Username = "mailer@example.com";
            o.Password = "<from-secret-manager>";   // never hard-code in production
            o.MaxConnections = 8;                    // pooled connections
            o.MaxMessagesPerConnection = 100;        // recycle after N messages
            o.IdleTimeoutSeconds = 120;              // close idle connections
            o.EhloHostname = "mailer.example.com";   // avoid leaking machine name
        }));

        // SmtpTransport is IAsyncDisposable-only — dispose the provider asynchronously.
        await using var provider = services.BuildServiceProvider();

        var transport = provider.GetRequiredService<IEmailTransport>();
        var options = provider.GetRequiredService<IOptions<SmtpTransportOptions>>().Value;

        Console.WriteLine($"  Transport : {transport.Name} ({transport.GetType().Name})");
        Console.WriteLine($"  Endpoint  : {options.Host}:{options.Port} (STARTTLS={options.UseSsl})");
        Console.WriteLine($"  Auth      : {options.AuthMethod} as {options.Username}");
        Console.WriteLine($"  Pool      : max {options.MaxConnections} conns, " +
                          $"{options.MaxMessagesPerConnection} msgs/conn, idle {options.IdleTimeoutSeconds}s");
        Console.WriteLine($"  EHLO host : {options.EhloHostname}");
        Console.WriteLine();
    }
}
