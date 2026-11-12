using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Extensions;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     NotificationsBuilder composes multiple channels in one registration. Priority then
///     drives fan-out among the channels each address belongs to: Critical uses every one of them,
///     and a channel no address belongs to gets nothing. This sample registers three channels and
///     shows a Critical send to an e-mail address and a phone number: one e-mail, one SMS, no push.
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

        // Critical priority → every registered channel each address belongs to: the e-mail address
        // by e-mail, the phone number by SMS. Push has no address here, so it gets nothing.
        await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.Admin,
            Recipient = new NotificationRecipient { EmailAddress = "oncall@example.com", PhoneNumber = "+390000000000" },
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
