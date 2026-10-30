using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Audit;
using Pragmatic.Audit.Engines.Sql;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The audit trail on the Sql engine (Pragmatic.Audit.Engines), on five databases: its tables as the generated schema has them (EF Core's
///     <c>__AuditEntries</c>, <c>__AuditSegments</c>, <c>__AuditPrunedRanges</c>), migrated with the others; the phase registered for a boundary
///     writing an entry for each write to an <c>[Audited]</c> entity in the same save; the store reading, sealing, verifying and pruning them.
/// </summary>
[Collection("pragmatic databases")]
public class AuditTrailTests(PragmaticDatabases databases)
{
    [Fact]
    public void The_trails_tables_in_the_schema_are_EF_Cores_column_for_column()
    {
        // SchemaMetadataTransform's audit tables on SQL Server, the EF Core form: a database where both write the trail holds one.
        var tables = DialectSchemas.For(SqlDialect.SqlServer).Tables;
        var entries = tables.Single(t => t.Name == "__AuditEntries");
        Assert.Equal(
            [("Seq", false), ("SegmentId", false), ("OccurredAt", false), ("Category", false), ("Operation", false), ("ActorRef", true), ("SubjectRef", true),
                ("TenantId", true), ("CorrelationId", true), ("BusinessOperation", true), ("OnBehalfOfRef", true), ("TargetType", true), ("TargetId", true),
                ("Outcome", false), ("ValueHash", true), ("Detail", true)],
            entries.Columns.Select(c => (c.Name, c.IsNullable)).OrderBy(c => Order(c.Name)));
        Assert.Equal(
            [("SegmentId", "nvarchar(32)"), ("OccurredAt", "bigint"), ("Operation", "nvarchar(128)"), ("BusinessOperation", "nvarchar(256)"), ("Detail", "nvarchar(2048)")],
            entries.Columns.Where(c => c.Name is "SegmentId" or "OccurredAt" or "Operation" or "BusinessOperation" or "Detail").Select(c => (c.Name, c.SqlType)).OrderBy(c => Order(c.Name)));
        Assert.Equal(["Seq"], entries.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name));
        Assert.Equal(["IX___AuditEntries_OccurredAt", "IX___AuditEntries_SegmentId_Seq"], entries.Indexes.Select(i => i.Name).Order());

        var segments = tables.Single(t => t.Name == "__AuditSegments");
        Assert.Equal(["SegmentId", "OpenedAt", "SealedAt", "EntryCount", "MerkleRoot", "PreviousHash", "SegmentHash"], segments.Columns.Select(c => c.Name));
        Assert.Equal(["IX___AuditSegments_SealedAt"], segments.Indexes.Select(i => i.Name));
        Assert.Equal(["FromSegmentId", "UntilSegmentId", "PrunedAt", "LinkHash"], tables.Single(t => t.Name == "__AuditPrunedRanges").Columns.Select(c => c.Name));

        static int Order(string column) => Array.IndexOf(["Seq", "SegmentId", "OccurredAt", "Category", "Operation", "ActorRef", "SubjectRef", "TenantId", "CorrelationId",
            "BusinessOperation", "OnBehalfOfRef", "TargetType", "TargetId", "Outcome", "ValueHash", "Detail"], column);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Each_write_to_an_audited_entity_appends_its_entry_and_the_store_seals_verifies_and_prunes_the_trail(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        var clock = new MovingClock(UnitOfWorkTests.Now);
        await using var provider = Trail(session, clock);

        var category = new Category { Name = "Lamps" };
        await using (var writes = provider.CreateAsyncScope())
        {
            // One unit of work across the four saves: it tracks the row as saved, so it tells a soft delete and a restore from an update.
            var (unitOfWork, repository) = UnitOfWork(writes, session);
            repository.Add(category);
            await unitOfWork.SaveChangesAsync();
            category.Name = "Lights";
            await unitOfWork.SaveChangesAsync();
            repository.Remove(category); // soft: a delete in the trail
            await unitOfWork.SaveChangesAsync();
            category.IsDeleted = false; // a restore
            await unitOfWork.SaveChangesAsync();
        }

        await using var scope = provider.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditTrailReader>();
        var page = await reader.QueryAsync(new AuditQuery { TargetId = category.PersistenceId.ToString() });
        Assert.Equal(4, page.TotalCount);
        Assert.Equal([AuditedChange.EntityRestored, AuditedChange.EntityDeleted, AuditedChange.EntityUpdated, AuditedChange.EntityCreated],
            page.Entries.Select(e => e.Operation)); // newest first
        Assert.All(page.Entries, e => Assert.Equal(("Category", "anna", AuditCategory.Data), (e.TargetType, e.ActorRef, e.Category)));
        Assert.Equal(0, (await reader.QueryAsync(new AuditQuery { TargetId = "nobody" })).TotalCount);

        // Sealed one grace period after its window closes, and verified intact.
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IAuditSealer>().SealDueSegmentsAsync());
        clock.Now += TimeSpan.FromDays(1);
        var sealedIds = await scope.ServiceProvider.GetRequiredService<IAuditSealer>().SealDueSegmentsAsync();
        var segment = Assert.Single(sealedIds);
        Assert.True((await reader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).IsIntact);

        // A write into the sealed segment's window is refused, and its save with it.
        var (lateUnitOfWork, lateRepository) = UnitOfWork(scope, session);
        lateRepository.Add(new Category { Name = "Late" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => lateUnitOfWork.SaveChangesAsync());

        // An entry altered after sealing is named.
        await session.Query<AuditEntryRow>().Where((e, op) => e.Operation == op, AuditedChange.EntityUpdated)
            .ExecuteUpdateAsync((s, op) => s.SetProperty(e => e.Operation, op), "Data.Tampered");
        Assert.Equal([segment], (await reader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).BrokenSegmentIds);

        // Retention discards the whole segment and declares the gap.
        var gap = await scope.ServiceProvider.GetRequiredService<IAuditRetention>().PruneOlderThanAsync(TimeSpan.FromHours(1));
        Assert.Equal((segment, segment), (gap!.FromSegmentId, gap.UntilSegmentId));
        Assert.Empty(await session.Query<AuditEntryRow>().ToListAsync());
        Assert.Equal([gap.FromSegmentId], (await reader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).PrunedRanges.Select(p => p.FromSegmentId));
    }

    /// <summary>A unit of work that runs the boundary's phases (this class stands for its key), and its categories.</summary>
    private static (SqlUnitOfWork UnitOfWork, IRepository<Category> Categories) UnitOfWork(AsyncServiceScope scope, SqlSession session)
    {
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope(),
            phases: scope.ServiceProvider.GetKeyedServices<ISqlSavePhase>(typeof(AuditTrailTests)));
        return (unitOfWork, new Category.SqlRepository(unitOfWork));
    }

    /// <summary>The trail of a boundary (this class stands for its key) on <paramref name="session" />, as the generated registration and the application add it.</summary>
    private static ServiceProvider Trail(SqlSession session, TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<SqlSession>(typeof(AuditTrailTests), session); // an instance: the scope does not dispose the test's session
        services.AddSingleton<ICurrentUser>(new TestUser("anna"));
        services.AddSingleton(clock);
        services.AddSqlAuditedEntities<AuditTrailTests>();
        services.AddSqlAuditTrail<AuditTrailTests>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>A clock a test moves: the instant the sealer and the retention read.</summary>
    private sealed class MovingClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
