using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Identity;
using Pragmatic.Messaging.Engines.Sql;
using Pragmatic.Messaging.Entities;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The Messaging outbox on the Sql engine (Pragmatic.Messaging.Engines), on five databases: its table as the generated schema has it (EF
///     Core's <c>__OutboxMessages</c>), migrated with the others; the phase registered for a boundary writing a save's events to it instead of
///     the unit of work dispatching them; the source the delivery pump reads claiming, marking, backing off and purging the rows on each
///     database's SQL.
/// </summary>
[Collection("pragmatic databases")]
public class MessagingOutboxTests(PragmaticDatabases databases)
{
    [Fact]
    public void The_outbox_table_in_the_schema_is_EF_Cores_column_for_column()
    {
        // SchemaMetadataTransform.BuildOutboxMessagesTable on SQL Server, the EF Core form of the table: a database where both write it holds one.
        var table = DialectSchemas.For(SqlDialect.SqlServer).Tables.Single(t => t.Name == "__OutboxMessages");
        Assert.Equal(
            [("Id", "uniqueidentifier", false), ("MessageType", "nvarchar(512)", false), ("Payload", "nvarchar(max)", false), ("CreatedAt", "bigint", false),
                ("ProcessedAt", "bigint", true), ("RetryCount", "int", false), ("NextAttemptAt", "bigint", true), ("Error", "nvarchar(2048)", true),
                ("ClaimedBy", "nvarchar(128)", true), ("ClaimedUntil", "bigint", true), ("CorrelationId", "nvarchar(64)", true), ("TenantId", "nvarchar(128)", true),
                ("UserId", "nvarchar(128)", true), ("HeadersJson", "nvarchar(max)", true)],
            table.Columns.Select(c => (c.Name, c.SqlType, c.IsNullable)).OrderBy(c => Order(c.Name)));
        Assert.Equal(["Id"], table.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name));
        var claim = Assert.Single(table.Indexes, i => i.Name == "IX___OutboxMessages_ProcessedAt_NextAttemptAt_ClaimedUntil_CreatedAt");
        Assert.Equal(["ProcessedAt", "NextAttemptAt", "ClaimedUntil", "CreatedAt"], claim.Columns.ToArray());
        var correlation = Assert.Single(table.Indexes, i => i.Name == "IX___OutboxMessages_CorrelationId");
        Assert.Equal(["CorrelationId"], correlation.Columns.ToArray());
        Assert.False(claim.IsUnique || correlation.IsUnique);

        static int Order(string column) => Array.IndexOf(["Id", "MessageType", "Payload", "CreatedAt", "ProcessedAt", "RetryCount", "NextAttemptAt", "Error",
            "ClaimedBy", "ClaimedUntil", "CorrelationId", "TenantId", "UserId", "HeadersJson"], column);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_save_writes_its_events_to_the_outbox_and_the_source_claims_marks_and_purges_them(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        var dispatcher = new RecordingDispatcher();
        var clock = new MovingClock(UnitOfWorkTests.Now);
        await using var provider = Outbox(session, clock);
        using (TenantScope.BeginScope("acme"))
        {
            await using var scope = provider.CreateAsyncScope();
            var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope(), dispatcher: dispatcher,
                phases: scope.ServiceProvider.GetKeyedServices<ISqlSavePhase>(typeof(MessagingOutboxTests)));
            IRepository<Amenity> amenities = new Amenity.SqlRepository(unitOfWork);
            amenities.Add(new Amenity { Name = "Spa" });
            amenities.Add(new Amenity { Name = "Gym" });
            await unitOfWork.SaveChangesAsync();
        }

        Assert.Empty(dispatcher.Dispatched); // in the outbox: the unit of work found none left to dispatch
        var written = await session.Query<OutboxMessageRow>().ToListAsync();
        Assert.Equal(2, written.Count);
        Assert.All(written, r => Assert.Equal(("acme", "anna", 0, (long?)null, typeof(AmenityChanged).FullName), (r.TenantId, r.UserId, r.RetryCount, r.ProcessedAt, r.MessageType)));

        await using var reading = provider.CreateAsyncScope();
        var source = Assert.Single(reading.ServiceProvider.GetServices<IOutboxSource>());
        Assert.Equal("Hotel", source.BoundaryName);
        Assert.Equal((2, (DateTimeOffset?)UnitOfWorkTests.Now), await source.InspectPendingAsync());
        Assert.Equal(2, (await source.PeekPendingAsync(10)).Count);

        var pending = await source.GetPendingAsync(10);
        Assert.Equal(2, pending.Count);
        Assert.Empty(await source.GetPendingAsync(10)); // both claimed, by this poll's token, until the lease runs out

        await source.MarkProcessedAsync(pending[0].Id);
        await source.MarkFailedAsync(pending[1].Id, "the broker refused it");
        var failed = await session.Query<OutboxMessageRow>().FirstOrDefaultAsync((r, id) => r.Id == id, pending[1].Id);
        Assert.NotNull(failed);
        Assert.Equal((1, "the broker refused it", (string?)null, (long?)null), (failed.RetryCount, failed.Error, failed.ClaimedBy, failed.ClaimedUntil));
        Assert.Equal((1, (DateTimeOffset?)UnitOfWorkTests.Now), await source.InspectPendingAsync());

        Assert.Empty(await source.GetPendingAsync(10)); // released, but inside its backoff window
        clock.Now += OutboxClaimPolicy.Backoff(1) + TimeSpan.FromSeconds(1);
        Assert.Equal([pending[1].Id], (await source.GetPendingAsync(10)).Select(m => m.Id));

        clock.Now += TimeSpan.FromHours(1);
        Assert.Equal(1, await source.PurgeProcessedAsync(TimeSpan.FromMinutes(30))); // the processed row; the pending one is never purged
        Assert.Equal([pending[1].Id], (await session.Query<OutboxMessageRow>().ToListAsync()).Select(r => r.Id));
    }

    /// <summary>The outbox of a boundary (this class stands for its key) on <paramref name="session" />, as the generated registration adds it.</summary>
    private static ServiceProvider Outbox(SqlSession session, TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<SqlSession>(typeof(MessagingOutboxTests), session); // an instance: the scope does not dispose the test's session
        services.AddSingleton<ITenantContext>(new TenantScope());
        services.AddSingleton<ICurrentUser>(new TestUser("anna"));
        services.AddSingleton(clock);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSqlMessagingOutbox<MessagingOutboxTests>("Hotel");
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>A clock a test moves: the instant the source's claim, backoff and purge read.</summary>
    private sealed class MovingClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
