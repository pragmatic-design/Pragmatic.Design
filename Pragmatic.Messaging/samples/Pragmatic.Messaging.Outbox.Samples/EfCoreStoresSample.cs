using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.EFCore;

namespace Pragmatic.Messaging.Outbox.Samples;

/// <summary>
///     Durable EF Core stores: <see cref="EfCoreIdempotencyStore" /> persists dedup keys via
///     <see cref="MessagingDbContext" /> (table: IdempotencyRecords).
///     Unlike the in-memory variants they survive restarts and
///     coordinate across processes. This runs against SQLite in-memory so it
///     executes self-contained; in production point MessagingDbContext at your
///     real provider (or call <see cref="MessagingDbContext.ApplyMessagingConfigurations" />
///     from your own DbContext to co-locate the tables).
/// </summary>
public static class EfCoreStoresSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- EF Core stores (idempotency + audit, SQLite) ---");

        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseSqlite(connection)
            .Options;

        // SqliteMessagingDbContext maps DateTimeOffset to long-ticks so the audit
        // query's ORDER BY Timestamp works on SQLite (which can't sort DateTimeOffset).
        await using var db = new SqliteMessagingDbContext(options);
        await db.Database.EnsureCreatedAsync();

        // Idempotency: first mark wins, redelivery is rejected — durably.
        var idempotency = new EfCoreIdempotencyStore(db, NullLogger<EfCoreIdempotencyStore>.Instance);
        var first = await idempotency.TryMarkAsProcessedAsync("msg-100");
        var duplicate = await idempotency.TryMarkAsProcessedAsync("msg-100");
        Console.WriteLine($"  TryMark first            : {first} (expected True)");
        Console.WriteLine($"  TryMark duplicate        : {duplicate} (expected False)");

        // The message audit trail lives in Pragmatic.Audit now, not here: it is one trail for the
        // whole application, verifiable, and with no field that could hold a serialized message.
        // See Pragmatic.Audit's README, and AuditingSample for the middleware that feeds it.
        Console.WriteLine();
    }

    /// <summary>
    ///     MessagingDbContext variant for SQLite: stores DateTimeOffset as long
    ///     ticks so ORDER BY on audit timestamps is translatable. EfCoreAuditStore
    ///     accepts any MessagingDbContext, so this subclass slots straight in.
    /// </summary>
    private sealed class SqliteMessagingDbContext(DbContextOptions<MessagingDbContext> options)
        : MessagingDbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        }
    }
}
