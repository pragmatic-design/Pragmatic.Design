using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Audit;
using Pragmatic.Audit.Engines.Documents;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     The audit trail of two boundaries with <c>[Audited]</c> entities on two engines, as the generated <c>Add{Boundary}Persistence</c>
///     registers their phases: each save appends its entries to its own database (the invoices' on SQLite, the letters' on MongoDB), and the
///     application reads, seals, verifies and prunes the trail of the one it names (the letters', on the documents engine).
/// </summary>
public sealed class AuditTrailOnTwoEnginesTests(MongoServer mongo) : IClassFixture<MongoServer>
{
    // The invoices' tables as SQLite holds them (the trail's are EF Core's: its instants are ticks, Seq the database's identity).
    private const string Schema = """
        CREATE TABLE "Invoices" ("PersistenceId" TEXT PRIMARY KEY, "Number" TEXT NOT NULL);
        CREATE TABLE "__AuditEntries" ("Seq" INTEGER PRIMARY KEY, "SegmentId" TEXT NOT NULL, "OccurredAt" INTEGER NOT NULL, "Category" INTEGER NOT NULL,
            "Operation" TEXT NOT NULL, "ActorRef" TEXT NULL, "SubjectRef" TEXT NULL, "TenantId" TEXT NULL, "CorrelationId" TEXT NULL,
            "BusinessOperation" TEXT NULL, "OnBehalfOfRef" TEXT NULL, "TargetType" TEXT NULL, "TargetId" TEXT NULL, "Outcome" INTEGER NOT NULL,
            "ValueHash" BLOB NULL, "Detail" TEXT NULL);
        CREATE TABLE "__AuditSegments" ("SegmentId" TEXT PRIMARY KEY, "OpenedAt" INTEGER NOT NULL, "SealedAt" INTEGER NULL, "EntryCount" INTEGER NOT NULL,
            "MerkleRoot" BLOB NULL, "PreviousHash" BLOB NULL, "SegmentHash" BLOB NULL);
        CREATE TABLE "__AuditPrunedRanges" ("FromSegmentId" TEXT PRIMARY KEY, "UntilSegmentId" TEXT NOT NULL, "PrunedAt" INTEGER NOT NULL, "LinkHash" BLOB NOT NULL);
        """;

    private static readonly DateTimeOffset Start = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Each_boundary_records_its_writes_in_its_database_and_the_named_trail_seals_verifies_and_prunes()
    {
        using var invoices = Invoices();
        var clock = new MovingClock(Start);
        await using var provider = Host(invoices.ConnectionString, Database(), clock);

        var letter = new Letter { Subject = "Renewal" };
        await using (var writes = provider.CreateAsyncScope())
        {
            var archive = writes.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(ArchiveBoundary));
            var letters = writes.ServiceProvider.GetRequiredService<IRepository<Letter>>();
            // A second apart: on the documents engine an entry's order is its instant, and entries of one instant are ordered by their id.
            letters.Add(letter);
            await archive.SaveChangesAsync();
            clock.Now += TimeSpan.FromSeconds(1);
            letter.Subject = "Renewal, signed";
            await archive.SaveChangesAsync();
            clock.Now += TimeSpan.FromSeconds(1);
            letters.Remove(letter);
            await archive.SaveChangesAsync();

            writes.ServiceProvider.GetRequiredService<IRepository<Invoice>>().Add(new Invoice { Number = "F-1" });
            await writes.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(LedgerBoundary)).SaveChangesAsync();
        }

        // The invoice's entry is in its own database, not in the letters'; the letters' entries are not in SQLite.
        Assert.Equal(1, Scalar(invoices, "SELECT COUNT(*) FROM \"__AuditEntries\" WHERE \"TargetType\" = 'Invoice' AND \"Operation\" = 'Data.EntityCreated' AND \"ActorRef\" = 'anna'"));
        Assert.Equal(0, Scalar(invoices, "SELECT COUNT(*) FROM \"__AuditEntries\" WHERE \"TargetType\" = 'Letter'"));

        await using var scope = provider.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditTrailReader>();
        var page = await reader.QueryAsync(new AuditQuery { TargetId = letter.PersistenceId.ToString() });
        Assert.Equal([AuditedChange.EntityDeleted, AuditedChange.EntityUpdated, AuditedChange.EntityCreated], page.Entries.Select(e => e.Operation)); // newest first
        Assert.All(page.Entries, e => Assert.Equal(("Letter", "anna", AuditCategory.Data), (e.TargetType, e.ActorRef, e.Category)));
        Assert.Equal(3, (await reader.QueryAsync(new AuditQuery())).TotalCount);

        // Sealed one grace period after its window closes, and verified intact.
        var sealer = scope.ServiceProvider.GetRequiredService<IAuditSealer>();
        Assert.Empty(await sealer.SealDueSegmentsAsync());
        clock.Now += TimeSpan.FromDays(1);
        var segment = Assert.Single(await sealer.SealDueSegmentsAsync());
        Assert.True((await reader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).IsIntact);

        // A write into the sealed segment's window is refused, and its save with it.
        clock.Now = Start;
        await using (var late = provider.CreateAsyncScope())
        {
            late.ServiceProvider.GetRequiredService<IRepository<Letter>>().Add(new Letter { Subject = "Late" });
            await Assert.ThrowsAsync<InvalidOperationException>(() => late.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(ArchiveBoundary)).SaveChangesAsync());
        }

        clock.Now = Start.AddDays(1);

        // An entry altered after sealing is named.
        var session = scope.ServiceProvider.GetRequiredKeyedService<DocSession>(typeof(ArchiveBoundary));
        await session.Query<AuditEntryDocument>().Where((e, op) => e.Operation == op, AuditedChange.EntityUpdated)
            .ExecuteUpdateAsync((u, op) => u.Set(e => e.Operation, op), "Data.Tampered");
        Assert.Equal([segment], (await reader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).BrokenSegmentIds);

        // Retention discards the whole segment and declares the gap.
        var gap = await scope.ServiceProvider.GetRequiredService<IAuditRetention>().PruneOlderThanAsync(TimeSpan.FromHours(1));
        Assert.Equal((segment, segment), (gap!.FromSegmentId, gap.UntilSegmentId));
        Assert.Empty(await session.Query<AuditEntryDocument>().ToListAsync());
        Assert.Equal([segment], (await reader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).PrunedRanges.Select(p => p.FromSegmentId));
    }

    private ServiceProvider Host(string invoices, string database, TimeProvider clock)
    {
        static SqliteConnection Open(string connectionString)
        {
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }

        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddSingleton<ICurrentUser>(new TestUser("anna"));
        services.AddSingleton<ITenantContext>(new TenantScope());
        services.AddScoped<IQueryFilterToggle, QueryFilterToggle>();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        services.AddLedgerBoundaryPersistence((_, filters) => new SqlSession(Open(invoices), SqlDialect.Sqlite) { Filters = filters });
        services.AddArchiveBoundaryPersistence((_, filters) => new DocSession(mongo.Client, database) { Filters = filters });
        // Two databases hold [Audited] entities: the application names the one its trail is read from, as with EF Core's AddAuditTrail.
        services.AddDocAuditTrail<ArchiveBoundary>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>An in-memory SQLite database of the test's own with the invoices' tables, kept alive by the returned connection.</summary>
    private static SqliteConnection Invoices()
    {
        var keeper = new SqliteConnection($"Data Source={Database()};Mode=Memory;Cache=Shared");
        keeper.Open();
        using var create = keeper.CreateCommand();
        create.CommandText = Schema;
        create.ExecuteNonQuery();
        return keeper;
    }

    private static string Database() => "a" + Guid.NewGuid().ToString("N")[..12];

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>A clock a test moves: the instant the saves, the sealer and the retention read.</summary>
    private sealed class MovingClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
