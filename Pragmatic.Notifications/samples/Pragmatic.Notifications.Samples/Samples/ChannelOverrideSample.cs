using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Extensions;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     NotificationRequest.ChannelOverride bypasses the router entirely: instead of letting
///     priority/preferences decide, the caller pins the exact channel(s). Because the property
///     is a [Flags] enum, an override can target several channels at once. This sample contrasts
///     a routed Normal send (router picks one) with an explicit Email|Push override.
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
        var recipient = new NotificationRecipient { EmailAddress = "user@example.com" };

        // No override: Normal priority lets the router pick the resolved channel (email here).
        await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = recipient,
            Content = content,
            Priority = NotificationPriority.Normal,
        });
        Console.WriteLine($"  routed (Normal)          : email={email.Count} push={push.Count} sms={sms.Count}");

        // Explicit override targeting two channels regardless of priority/preferences.
        await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = recipient,
            Content = content,
            Priority = NotificationPriority.Low,
            ChannelOverride = NotificationChannel.Email | NotificationChannel.Push,
        });
        Console.WriteLine($"  override Email|Push      : email={email.Count} push={push.Count} sms={sms.Count}");
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
