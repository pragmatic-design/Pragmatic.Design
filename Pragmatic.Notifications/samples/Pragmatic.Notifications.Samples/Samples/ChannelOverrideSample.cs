using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Extensions;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     NotificationRequest.ChannelOverride bypasses the router entirely: instead of letting
///     priority/preferences decide, the caller pins the exact channel(s). Because the property
///     is a [Flags] enum, an override can target several channels at once. It pins channels, not
///     addresses: each recipient receives only the pinned channels its address belongs to. This sample
///     contrasts a routed Normal send with an Sms override and an Email|Sms override, to a recipient
///     that has both an e-mail address and a phone number.
/// </summary>
public static class ChannelOverrideSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- ChannelOverride ---");

        var email = new CountingChannel(NotificationChannel.Email);
        var push = new CountingChannel(NotificationChannel.Push);
        var sms = new CountingChannel(NotificationChannel.Sms);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticNotifications(n => n
            .AddChannel(email)
            .AddChannel(push)
            .AddChannel(sms)
            .UseInMemoryStore());

        await using var provider = services.BuildServiceProvider();
        var notifications = provider.GetRequiredService<INotificationService>();

        var content = new NotificationContent { Subject = "Account updated", Body = "Your settings changed." };
        var recipient = new NotificationRecipient { EmailAddress = "user@example.com", PhoneNumber = "+390000000000" };

        // No override: Normal priority lets the router pick each address's own channel.
        await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = recipient,
            Content = content,
            Priority = NotificationPriority.Normal,
        });
        Console.WriteLine($"  routed (Normal)          : email={email.Count} push={push.Count} sms={sms.Count}");

        // Sms only: the phone number receives it, the e-mail address is not handed to the SMS provider.
        await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = recipient,
            Content = content,
            Priority = NotificationPriority.Low,
            ChannelOverride = NotificationChannel.Sms,
        });
        Console.WriteLine($"  override Sms             : email={email.Count} push={push.Count} sms={sms.Count}");

        // Two channels regardless of priority/preferences: one e-mail and one SMS, each to its own address.
        await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = recipient,
            Content = content,
            Priority = NotificationPriority.Low,
            ChannelOverride = NotificationChannel.Email | NotificationChannel.Sms,
        });
        Console.WriteLine($"  override Email|Sms       : email={email.Count} push={push.Count} sms={sms.Count}");
        Console.WriteLine();
    }

    private sealed class CountingChannel(NotificationChannel channel) : INotificationChannel
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
