using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.EFCore;
using Pragmatic.Notifications.Extensions;
using Pragmatic.Notifications.Tracking;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     UseEfCoreStore swaps the in-memory tracking store for EfCoreNotificationStore, which persists
///     every delivery attempt to the __Notifications table via an IDbContextFactory. This sample uses
///     a SQLite connection (kept open so the in-memory database survives) to create the schema, send a
///     notification through the pipeline, and then read the persisted NotificationRecord back —
///     including its serialized Metadata — proving the tracking row was written and updated to Sent.
///     In production the configureDb callback would call UseNpgsql / UseSqlServer instead.
/// </summary>
public static class EfCoreStoreSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- EF Core notification store (SQLite) ---");

        // A single open connection backs the in-memory SQLite database for the sample's lifetime.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var email = new RecordingChannel();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticNotifications(n => n
            .AddChannel(email)
            .UseEfCoreStore(db => db.UseSqlite(connection)));

        await using var provider = services.BuildServiceProvider();

        // Create the schema (production apps use migrations).
        var factory = provider.GetRequiredService<IDbContextFactory<NotificationDbContext>>();
        await using (var ctx = await factory.CreateDbContextAsync())
            await ctx.Database.EnsureCreatedAsync();

        var notifications = provider.GetRequiredService<INotificationService>();
        var store = provider.GetRequiredService<INotificationStore>();

        var result = await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = new NotificationRecipient { EmailAddress = "persist@example.com" },
            Content = new NotificationContent { Subject = "Saved to DB", Body = "This delivery is tracked." },
            Category = "transactional",
            Metadata = new Dictionary<string, string> { ["orderId"] = "ORD-42" },
        });

        Console.WriteLine($"  send result        : {(result.Success ? "OK" : "FAIL")}");
        Console.WriteLine($"  delivery record ids: {result.DeliveryIds?.Count ?? 0}");

        var id = result.DeliveryIds![0];
        var record = await store.GetByIdAsync(id);
        Console.WriteLine($"  loaded from store  : {record is not null}");
        if (record is not null)
        {
            Console.WriteLine($"    address  : {record.RecipientAddress}");
            Console.WriteLine($"    channel  : {record.Channel}");
            Console.WriteLine($"    status   : {record.Status}");
            Console.WriteLine($"    metadata : orderId={record.Metadata?.GetValueOrDefault("orderId")}");
        }
        Console.WriteLine();
    }

    private sealed class RecordingChannel : INotificationChannel
    {
        public NotificationChannel Channel => NotificationChannel.Email;

        public Task<DeliveryResult> DeliverAsync(
            ResolvedRecipient recipient,
            NotificationContent content,
            CancellationToken ct = default)
            => Task.FromResult(DeliveryResult.Succeeded());
    }
}
