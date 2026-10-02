using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Extensions;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     The built-in SMTP channel (Pragmatic.Notifications.Email.SmtpChannel) delegates to
///     Pragmatic.Email's IEmailSender for real SMTP delivery. Wiring it up in production looks like:
///
///     <code>
///     services.AddPragmaticNotifications(n => n
///         .AddSmtp(
///             sender =>    // SmtpOptions: From identity for the notification channel
///             {
///                 sender.SenderAddress = "no-reply@example.com";
///                 sender.SenderName    = "Acme";
///             },
///             transport => // Pragmatic.Email.SmtpTransportOptions: host/port/credentials
///             {
///                 transport.Host = "smtp.example.com";
///                 transport.Port = 587;
///                 // transport.Username / transport.Password ...
///             }));
///     </code>
///
///     That requires a reachable SMTP server, so this sample instead registers a fake Email
///     channel that captures the message in memory. It demonstrates the exact channel contract
///     SmtpChannel implements (INotificationChannel for NotificationChannel.Email) and how an
///     email notification — including its HTML body — flows through the pipeline.
/// </summary>
public static class SmtpChannelSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- SMTP / Email channel (fake sender) ---");

        var smtp = new FakeSmtpChannel();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticNotifications(n => n
            .AddChannel(smtp)         // stands in for .AddSmtp(sender, transport)
            .UseInMemoryStore());

        await using var provider = services.BuildServiceProvider();
        var notifications = provider.GetRequiredService<INotificationService>();

        var result = await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = new NotificationRecipient { EmailAddress = "customer@example.com" },
            Content = new NotificationContent
            {
                Subject = "Your invoice is ready",
                Body = "Invoice INV-9001 totalling EUR 120.00 is attached.",
                HtmlBody = "<h1>Invoice INV-9001</h1><p>Total: <strong>EUR 120.00</strong></p>",
            },
        });

        Console.WriteLine($"  send result   : {(result.Success ? "OK" : "FAIL")}");
        if (smtp.LastMessage is { } msg)
        {
            Console.WriteLine($"  to            : {msg.To}");
            Console.WriteLine($"  subject       : {msg.Subject}");
            Console.WriteLine($"  has html body : {msg.HasHtml}");
        }
        Console.WriteLine();
    }

    /// <summary>
    ///     Mirrors SmtpChannel: handles NotificationChannel.Email and "sends" the message.
    ///     The real channel builds an EmailMessage and calls IEmailSender.SendAsync.
    /// </summary>
    private sealed class FakeSmtpChannel : INotificationChannel
    {
        public CapturedMessage? LastMessage { get; private set; }

        public NotificationChannel Channel => NotificationChannel.Email;

        public Task<DeliveryResult> DeliverAsync(
            ResolvedRecipient recipient,
            NotificationContent content,
            CancellationToken ct = default)
        {
            LastMessage = new CapturedMessage(recipient.Address, content.Subject, content.HtmlBody is not null);
            // A real SMTP send returns the provider message id on success.
            return Task.FromResult(DeliveryResult.Succeeded(providerId: $"<{Guid.NewGuid():N}@example.com>"));
        }
    }

    private sealed record CapturedMessage(string To, string Subject, bool HasHtml);
}
