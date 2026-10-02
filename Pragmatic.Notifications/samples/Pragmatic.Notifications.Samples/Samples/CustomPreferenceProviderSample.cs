using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Extensions;
using Pragmatic.Notifications.Preferences;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     A custom <see cref="INotificationPreferenceProvider"/> lets users opt out of categories or
///     disable a channel. The DefaultRecipientResolver attaches the resolved preferences to each
///     direct-email recipient, and DefaultNotificationRouter honours them: a muted category routes
///     to NotificationChannel.None (no delivery), while allowed categories proceed. This sample
///     shows a user who has muted "marketing" but still receives "transactional" mail.
/// </summary>
public static class CustomPreferenceProviderSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- Custom INotificationPreferenceProvider (category muting) ---");

        var email = new RecordingChannel(NotificationChannel.Email);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticNotifications(n => n
            .AddChannel(email)
            .UsePreferences<MutingPreferenceProvider>()
            .UseInMemoryStore());

        await using var provider = services.BuildServiceProvider();
        var notifications = provider.GetRequiredService<INotificationService>();

        var recipient = new NotificationRecipient { EmailAddress = "alice@example.com" };

        // Transactional: allowed by preferences → delivered.
        await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = recipient,
            Content = new NotificationContent { Subject = "Receipt #1001", Body = "Thanks for your order." },
            Category = "transactional",
        });

        // Marketing: muted by preferences → router returns None, no delivery.
        await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = recipient,
            Content = new NotificationContent { Subject = "Spring sale!", Body = "20% off." },
            Category = "marketing",
        });

        Console.WriteLine($"  delivered subjects : {email.Subjects.Count}");
        foreach (var s in email.Subjects)
            Console.WriteLine($"    -> {s}");
        Console.WriteLine("  (marketing was suppressed by the user's muted-category preference)");
        Console.WriteLine();
    }

    /// <summary>Marks "alice@example.com" as having muted the "marketing" category.</summary>
    private sealed class MutingPreferenceProvider : INotificationPreferenceProvider
    {
        public Task<NotificationPreferences?> GetPreferencesAsync(string userIdOrAddress, CancellationToken ct = default)
        {
            var prefs = userIdOrAddress == "alice@example.com"
                ? new NotificationPreferences
                {
                    Enabled = true,
                    PreferredChannel = NotificationChannel.Email,
                    MutedCategories = new HashSet<string> { "marketing" },
                }
                : new NotificationPreferences();

            return Task.FromResult<NotificationPreferences?>(prefs);
        }
    }

    private sealed class RecordingChannel(NotificationChannel channel) : INotificationChannel
    {
        public List<string> Subjects { get; } = [];

        public NotificationChannel Channel => channel;

        public Task<DeliveryResult> DeliverAsync(
            ResolvedRecipient recipient,
            NotificationContent content,
            CancellationToken ct = default)
        {
            Subjects.Add(content.Subject);
            return Task.FromResult(DeliveryResult.Succeeded());
        }
    }
}
