using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Extensions;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     NotificationsBuilder composes multiple channels in one registration. Priority then
///     drives fan-out: Critical reaches every registered channel, Normal uses the recipient's
///     resolved channel (or email fallback). This sample registers three channels and shows
///     how a single Critical send fans out across all of them via the real routing pipeline.
/// </summary>
public static class MultiChannelConfigurationSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- NotificationsBuilder multi-channel configuration ---");

        var email = new RecordingChannel(NotificationChannel.Email);
        var sms = new RecordingChannel(NotificationChannel.Sms);
        var push = new RecordingChannel(NotificationChannel.Push);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticNotifications(n => n
            .AddChannel(email)
            .AddChannel(sms)
            .AddChannel(push)
            .UseInMemoryStore()
            .Configure(o => o.DeliveryChannelCapacity = 256));

        await using var provider = services.BuildServiceProvider();
        var notifications = provider.GetRequiredService<INotificationService>();
        var factory = provider.GetRequiredService<INotificationChannelFactory>();

        Console.WriteLine($"  registered channels : {string.Join(", ", factory.GetRegisteredChannels())}");

        // Critical priority → router combines ALL registered channels.
        await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.Admin,
            Recipient = new NotificationRecipient { EmailAddress = "oncall@example.com" },
            Content = new NotificationContent { Subject = "Datastore unreachable", Body = "Failover triggered." },
            Priority = NotificationPriority.Critical,
            Category = "ops-alert",
        });

        Console.WriteLine($"  email deliveries     : {email.Count}");
        Console.WriteLine($"  sms deliveries       : {sms.Count}");
        Console.WriteLine($"  push deliveries      : {push.Count}");
        Console.WriteLine();
    }

    private sealed class RecordingChannel(NotificationChannel channel) : INotificationChannel
    {
        public int Count { get; private set; }

        public NotificationChannel Channel => channel;

        public Task<DeliveryResult> DeliverAsync(
            ResolvedRecipient recipient,
            NotificationContent content,
            CancellationToken ct = default)
        {
            Count++;
            return Task.FromResult(DeliveryResult.Succeeded());
        }
    }
}
