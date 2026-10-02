using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Extensions;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     A custom delivery channel is any class implementing <see cref="INotificationChannel"/>.
///     It declares the <see cref="NotificationChannel"/> flag it handles and delivers content,
///     returning a <see cref="DeliveryResult"/>. Register it via the builder's AddChannel.
///     This sample wires a tiny in-memory "SMS" channel through the real DI pipeline and
///     sends a notification end to end — no external infrastructure.
/// </summary>
public static class CustomChannelSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- Custom INotificationChannel (in-memory SMS) ---");

        var sms = new InMemorySmsChannel();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticNotifications(n => n
            .AddChannel(sms)          // register our channel instance as INotificationChannel
            .UseInMemoryStore());

        await using var provider = services.BuildServiceProvider();
        var notifications = provider.GetRequiredService<INotificationService>();

        // No automatic user-ID resolution is wired, so target a direct phone number.
        // ChannelOverride forces routing to the SMS channel.
        var result = await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = new NotificationRecipient { PhoneNumber = "+15551234567" },
            Content = new NotificationContent
            {
                Subject = "2FA code",
                Body = "Your verification code is 481920.",
                ShortBody = "Code: 481920",
            },
            ChannelOverride = NotificationChannel.Sms,
        });

        Console.WriteLine($"  send result      : {(result.Success ? "OK" : "FAIL")}");
        Console.WriteLine($"  messages sent    : {sms.Sent.Count}");
        foreach (var (address, subject) in sms.Sent)
            Console.WriteLine($"    -> {address}: \"{subject}\"");
        Console.WriteLine();
    }

    /// <summary>
    ///     Minimal channel that records deliveries in a list instead of contacting a real SMS gateway.
    ///     A production channel would call an SDK (Twilio, Vonage, ...) inside DeliverAsync.
    /// </summary>
    private sealed class InMemorySmsChannel : INotificationChannel
    {
        public List<(string Address, string Subject)> Sent { get; } = [];

        public NotificationChannel Channel => NotificationChannel.Sms;

        public Task<DeliveryResult> DeliverAsync(
            ResolvedRecipient recipient,
            NotificationContent content,
            CancellationToken ct = default)
        {
            Sent.Add((recipient.Address, content.Subject));
            return Task.FromResult(DeliveryResult.Succeeded(providerId: $"sms-{Guid.NewGuid():N}"));
        }
    }
}
