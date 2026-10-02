using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Extensions;
using Pragmatic.Notifications.Pipeline;
using Pragmatic.Notifications.Preferences;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     The built-in DefaultRecipientResolver only handles direct addresses; it cannot turn a
///     UserId into an email. A custom <see cref="IRecipientResolver"/> bridges that gap — typically
///     by querying Identity or a user-profile store. This sample registers a resolver backed by an
///     in-memory user directory so that NotificationRecipient.User("...") and .Users([...]) actually
///     resolve to concrete email addresses and get delivered.
/// </summary>
public static class CustomRecipientResolverSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- Custom IRecipientResolver (UserId -> email) ---");

        var email = new RecordingChannel(NotificationChannel.Email);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticNotifications(n => n
            .AddChannel(email)
            .UseInMemoryStore());

        // Override the default resolver. AddPragmaticNotifications uses TryAdd for core
        // services, so registering ours first wins.
        services.AddSingleton<IRecipientResolver, DirectoryRecipientResolver>();

        await using var provider = services.BuildServiceProvider();
        var notifications = provider.GetRequiredService<INotificationService>();

        // Single user by ID — now resolvable.
        await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.User("u-1"),
            Content = new NotificationContent { Subject = "Welcome", Body = "Hi Alice!" },
        });

        // Multiple users in one request.
        await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.Users(["u-1", "u-2", "u-unknown"]),
            Content = new NotificationContent { Subject = "Team update", Body = "Standup at 10." },
        });

        Console.WriteLine($"  emails delivered : {email.Addresses.Count}");
        foreach (var addr in email.Addresses)
            Console.WriteLine($"    -> {addr}");
        Console.WriteLine("  (u-unknown was not in the directory, so it produced no delivery)");
        Console.WriteLine();
    }

    /// <summary>Resolves user IDs to email addresses from a static directory; delegates direct addresses.</summary>
    private sealed class DirectoryRecipientResolver : IRecipientResolver
    {
        private static readonly Dictionary<string, string> Directory = new()
        {
            ["u-1"] = "alice@example.com",
            ["u-2"] = "bob@example.com",
        };

        public Task<IReadOnlyList<ResolvedRecipient>> ResolveAsync(
            NotificationRecipient recipient,
            NotificationAudience audience,
            CancellationToken ct = default)
        {
            var results = new List<ResolvedRecipient>();

            if (recipient.EmailAddress is not null)
                results.Add(new ResolvedRecipient(recipient.EmailAddress, NotificationChannel.Email, null, null, new NotificationPreferences()));

            if (recipient.UserId is not null)
                AddUser(recipient.UserId, results);

            if (recipient.UserIds is { Count: > 0 })
                foreach (var id in recipient.UserIds)
                    AddUser(id, results);

            return Task.FromResult<IReadOnlyList<ResolvedRecipient>>(results);
        }

        private static void AddUser(string userId, List<ResolvedRecipient> results)
        {
            if (Directory.TryGetValue(userId, out var address))
                results.Add(new ResolvedRecipient(address, NotificationChannel.Email, null, null, new NotificationPreferences()));
        }
    }

    private sealed class RecordingChannel(NotificationChannel channel) : INotificationChannel
    {
        public List<string> Addresses { get; } = [];

        public NotificationChannel Channel => channel;

        public Task<DeliveryResult> DeliverAsync(
            ResolvedRecipient recipient,
            NotificationContent content,
            CancellationToken ct = default)
        {
            Addresses.Add(recipient.Address);
            return Task.FromResult(DeliveryResult.Succeeded());
        }
    }
}
