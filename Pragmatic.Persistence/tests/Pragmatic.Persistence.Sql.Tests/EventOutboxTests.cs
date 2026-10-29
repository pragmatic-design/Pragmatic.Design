using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Events;
using Pragmatic.Events.Engines;
using Pragmatic.Events.Engines.Sql;
using Pragmatic.Events.Outbox;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The event outbox on the Sql engine (Pragmatic.Events.Engines), on five databases: its table as the generated schema has it (EF Core's
///     <c>__EventOutbox</c>), migrated with the others; the phase registered for a boundary writing a save's events to it instead of the unit of
///     work dispatching them; the drainer claiming, delivering and marking the rows on each database's SQL.
/// </summary>
[Collection("pragmatic databases")]
public class EventOutboxTests(PragmaticDatabases databases)
{
    [Fact]
    public void The_outbox_table_in_the_schema_is_EF_Cores_column_for_column()
    {
        // SchemaMetadataTransform.BuildEventOutboxTable on SQL Server, the EF Core form of the table: a database where both write it holds one.
        var table = DialectSchemas.For(SqlDialect.SqlServer).Tables.Single(t => t.Name == "__EventOutbox");
        Assert.Equal(
            [("Id", "uniqueidentifier", false), ("EventType", "nvarchar(512)", false), ("Payload", "nvarchar(max)", false), ("OccurredAt", "bigint", false),
                ("TenantId", "nvarchar(128)", true), ("TraceParent", "nvarchar(64)", true), ("CreatedAt", "bigint", false), ("ProcessedAt", "bigint", true),
                ("Attempts", "int", false), ("LastError", "nvarchar(max)", true), ("ClaimedBy", "nvarchar(128)", true), ("ClaimedUntil", "bigint", true)],
            table.Columns.Select(c => (c.Name, c.SqlType, c.IsNullable)).OrderBy(c => Order(c.Name)));
        Assert.Equal(["Id"], table.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name));
        var claim = Assert.Single(table.Indexes, i => i.Name == "IX___EventOutbox_ProcessedAt_ClaimedUntil_CreatedAt");
        Assert.Equal(["ProcessedAt", "ClaimedUntil", "CreatedAt"], claim.Columns.ToArray());
        Assert.False(claim.IsUnique);

        static int Order(string column) => Array.IndexOf(["Id", "EventType", "Payload", "OccurredAt", "TenantId", "TraceParent", "CreatedAt", "ProcessedAt", "Attempts",
            "LastError", "ClaimedBy", "ClaimedUntil"], column);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_save_writes_its_events_to_the_outbox_and_the_drainer_delivers_each_once(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        var dispatcher = new RecordingDispatcher();
        await using var provider = Outbox(session, dispatcher);
        using (TenantScope.BeginScope("acme"))
        {
            await using var scope = provider.CreateAsyncScope();
            var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope(), dispatcher: dispatcher,
                phases: scope.ServiceProvider.GetKeyedServices<ISqlSavePhase>(typeof(EventOutboxTests)));
            IRepository<Amenity> amenities = new Amenity.SqlRepository(unitOfWork);
            amenities.Add(new Amenity { Name = "Spa" });
            amenities.Add(new Amenity { Name = "Gym" });
            await unitOfWork.SaveChangesAsync();
        }

        Assert.Empty(dispatcher.Dispatched); // in the outbox: the unit of work found none left to dispatch
        var written = await session.Query<EventOutboxRow>().OrderBy(r => r.EventType).ToListAsync();
        Assert.Equal([("acme", 0, (long?)null), ("acme", 0, null)], written.Select(r => (r.TenantId, r.Attempts, r.ProcessedAt)));

        var drainer = provider.GetRequiredService<IEngineEventOutboxDrainer<EventOutboxTests>>();
        Assert.Equal(2, await drainer.DrainOnceAsync());
        Assert.Equal(0, await drainer.DrainOnceAsync());
        Assert.Equal(["Gym", "Spa"], dispatcher.Dispatched.Cast<AmenityChanged>().Where(e => e.Lifecycle == EntityLifecycle.Created).Select(e => e.Name).Order());
        Assert.All(await session.Query<EventOutboxRow>().ToListAsync(), r => Assert.NotNull(r.ProcessedAt)); // the claim stays, as EF Core's drainer leaves it
    }

    /// <summary>The outbox of a boundary (this class stands for its key) on <paramref name="session" />, as the generated registration adds it.</summary>
    private static ServiceProvider Outbox(SqlSession session, RecordingDispatcher dispatcher)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<SqlSession>(typeof(EventOutboxTests), session); // an instance: the scope does not dispose the test's session
        services.AddSingleton<IDomainEventDispatcher>(dispatcher);
        services.AddSingleton<IEventOutboxTypeResolver>(new EventOutboxTypeResolver([typeof(AmenityChanged)]));
        services.AddSingleton<ITenantContext>(new TenantScope());
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSqlEventOutbox<EventOutboxTests>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
