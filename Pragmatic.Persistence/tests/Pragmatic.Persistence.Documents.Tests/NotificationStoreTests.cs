using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Engines.Documents;
using Pragmatic.Notifications.Tracking;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Schema;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     The notification store on the documents engine (Pragmatic.Notifications.Engines), on MongoDB and Cosmos DB: its collection created from
///     the schema the package declares (EF Core's indexes, by name), and the four operations of <see cref="INotificationStore" /> through the
///     store the boundary's generated registration adds. The instants read back to the tick: the document keeps them as UTC ticks.
/// </summary>
[Collection("document servers")]
public class NotificationStoreTests(DocumentServers servers)
{
    private static readonly DateTimeOffset Created = new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero).AddTicks(1234567);

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_record_is_created_read_moved_through_its_statuses_and_left_pending_until_it_is_sent(Store store)
    {
        var session = servers.Session(store);
        var schema = await DocSchema.EnsureAsync(session, [NotificationDocument.Schema]);
        // MongoDB creates the indexes by name; Cosmos DB indexes every field of a container, so what it creates is the container.
        var created = store == Store.Mongo ? "IX___Notifications_Status_CreatedAt" : "container";
        Assert.Contains(schema.Changes, c => c.Collection == "__Notifications" && c.Element == created && c.Outcome == DocSchemaOutcome.Created);
        var clock = new MovingClock(Created.AddMinutes(5));
        await using var provider = Provider(session, clock);
        var notifications = provider.GetRequiredService<INotificationStore>();
        Assert.IsType<DocNotificationStore>(notifications);

        var later = Record("later@example.com", Created.AddSeconds(2)) with { Metadata = new() { ["order"] = "A-1" }, TenantId = "acme", Category = "orders" };
        var first = Record("first@example.com", Created);
        var sent = Record("sent@example.com", Created.AddSeconds(1)) with { Status = DeliveryStatus.Sent, SentAt = Created.AddSeconds(1) };
        foreach (var record in new[] { later, first, sent })
            Assert.Same(record, await notifications.CreateAsync(record));

        var read = await notifications.GetByIdAsync(later.Id);
        Assert.NotNull(read);
        Assert.Equal((later.Audience, later.RecipientAddress, later.Channel, later.Subject, later.Status, later.TenantId, later.Category, later.CreatedAt),
            (read.Audience, read.RecipientAddress, read.Channel, read.Subject, read.Status, read.TenantId, read.Category, read.CreatedAt));
        Assert.Equal(later.Metadata, read.Metadata);
        Assert.Null(await notifications.GetByIdAsync(Guid.NewGuid()));

        // Pending only, oldest first, as many as asked.
        Assert.Equal([first.Id, later.Id], (await notifications.GetPendingAsync(10)).Select(r => r.Id));
        Assert.Equal([first.Id], (await notifications.GetPendingAsync(1)).Select(r => r.Id));

        // Each status stamps its own instant and keeps the earlier ones; the error is written as given.
        await notifications.UpdateStatusAsync(first.Id, DeliveryStatus.Sent, null);
        clock.Now += TimeSpan.FromMinutes(1);
        await notifications.UpdateStatusAsync(first.Id, DeliveryStatus.Delivered, null);
        clock.Now += TimeSpan.FromMinutes(1);
        await notifications.UpdateStatusAsync(first.Id, DeliveryStatus.Failed, "the provider bounced it");
        var moved = await notifications.GetByIdAsync(first.Id);
        Assert.NotNull(moved);
        Assert.Equal((DeliveryStatus.Failed, "the provider bounced it", (DateTimeOffset?)Created.AddMinutes(5), (DateTimeOffset?)Created.AddMinutes(6), (DateTimeOffset?)null),
            (moved.Status, moved.ErrorMessage, moved.SentAt, moved.DeliveredAt, moved.ReadAt));
        Assert.Equal([later.Id], (await notifications.GetPendingAsync(10)).Select(r => r.Id));

        await notifications.UpdateStatusAsync(Guid.NewGuid(), DeliveryStatus.Sent, null); // a record that is not there: logged, nothing written
        Assert.Equal(3, (await session.Query<NotificationDocument>().ToListAsync()).Count);
    }

    private static NotificationRecord Record(string address, DateTimeOffset created) => new()
    {
        Audience = NotificationAudience.EndUser,
        RecipientAddress = address,
        Channel = NotificationChannel.Email,
        Subject = "Your order",
        Status = DeliveryStatus.Pending,
        CreatedAt = created,
    };

    /// <summary>The store of a boundary (this class stands for its key) on <paramref name="session" />, as the generated registration adds it.</summary>
    private static ServiceProvider Provider(DocSession session, TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<DocSession>(typeof(NotificationStoreTests), session);
        services.AddSingleton(clock);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddDocNotificationStore<NotificationStoreTests>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>A clock a test moves: the instant a status transition stamps.</summary>
    private sealed class MovingClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
