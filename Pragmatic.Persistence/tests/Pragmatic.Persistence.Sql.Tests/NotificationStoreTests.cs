using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Engines.Sql;
using Pragmatic.Notifications.Tracking;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The notification store on the Sql engine (Pragmatic.Notifications.Engines), on five databases: its table as the generated schema has it
///     (EF Core's <c>__Notifications</c>, compared column for column by <c>TheSqlEnginesSchemaIsPragmaticsTests</c>), migrated with the others,
///     and the four operations of <see cref="INotificationStore" /> through the store the boundary's generated registration adds.
/// </summary>
[Collection("pragmatic databases")]
public class NotificationStoreTests(PragmaticDatabases databases)
{
    private static readonly DateTimeOffset Created = new(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_record_is_created_read_moved_through_its_statuses_and_left_pending_until_it_is_sent(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        var clock = new MovingClock(Created.AddMinutes(5));
        await using var provider = Store(session, clock);
        var store = provider.GetRequiredService<INotificationStore>();
        Assert.IsType<SqlNotificationStore>(store);

        var later = Record("later@example.com", Created.AddSeconds(2)) with { Metadata = new() { ["order"] = "A-1" }, TenantId = "acme", Category = "orders" };
        var first = Record("first@example.com", Created);
        var sent = Record("sent@example.com", Created.AddSeconds(1)) with { Status = DeliveryStatus.Sent, SentAt = Created.AddSeconds(1) };
        foreach (var record in new[] { later, first, sent })
            Assert.Same(record, await store.CreateAsync(record));

        var read = await store.GetByIdAsync(later.Id);
        Assert.NotNull(read);
        Assert.Equal((later.Audience, later.RecipientAddress, later.Channel, later.Subject, later.Status, later.TenantId, later.Category, later.CreatedAt),
            (read.Audience, read.RecipientAddress, read.Channel, read.Subject, read.Status, read.TenantId, read.Category, read.CreatedAt));
        Assert.Equal(later.Metadata, read.Metadata);
        Assert.Null(await store.GetByIdAsync(Guid.NewGuid()));

        // Pending only, oldest first, as many as asked.
        Assert.Equal([first.Id, later.Id], (await store.GetPendingAsync(10)).Select(r => r.Id));
        Assert.Equal([first.Id], (await store.GetPendingAsync(1)).Select(r => r.Id));

        // Each status stamps its own instant and keeps the earlier ones; the error is written as given.
        await store.UpdateStatusAsync(first.Id, DeliveryStatus.Sent, null);
        clock.Now += TimeSpan.FromMinutes(1);
        await store.UpdateStatusAsync(first.Id, DeliveryStatus.Delivered, null);
        clock.Now += TimeSpan.FromMinutes(1);
        await store.UpdateStatusAsync(first.Id, DeliveryStatus.Failed, "the provider bounced it");
        var moved = await store.GetByIdAsync(first.Id);
        Assert.NotNull(moved);
        Assert.Equal((DeliveryStatus.Failed, "the provider bounced it", (DateTimeOffset?)Created.AddMinutes(5), (DateTimeOffset?)Created.AddMinutes(6), (DateTimeOffset?)null),
            (moved.Status, moved.ErrorMessage, moved.SentAt, moved.DeliveredAt, moved.ReadAt));
        Assert.Equal([later.Id], (await store.GetPendingAsync(10)).Select(r => r.Id));

        await store.UpdateStatusAsync(Guid.NewGuid(), DeliveryStatus.Sent, null); // a record that is not there: logged, nothing written
        Assert.Equal(3, (await session.Query<NotificationRow>().ToListAsync()).Count);
    }

    internal static NotificationRecord Record(string address, DateTimeOffset created) => new()
    {
        Audience = NotificationAudience.EndUser,
        RecipientAddress = address,
        Channel = NotificationChannel.Email,
        Subject = "Your order",
        Status = DeliveryStatus.Pending,
        CreatedAt = created,
    };

    /// <summary>The store of a boundary (this class stands for its key) on <paramref name="session" />, as the generated registration adds it.</summary>
    private static ServiceProvider Store(SqlSession session, TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<SqlSession>(typeof(NotificationStoreTests), session); // an instance: the scope does not dispose the test's session
        services.AddSingleton(clock);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSqlNotificationStore<NotificationStoreTests>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>A clock a test moves: the instant a status transition stamps.</summary>
    private sealed class MovingClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
