using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Engines.Sql;
using Pragmatic.Messaging.Generated;
using Pragmatic.Messaging.Saga;
using Pragmatic.Persistence.Sql;
using PragmaticSagas.Tests;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The saga store on the Sql engine (Pragmatic.Messaging.Engines), on five databases: <see cref="StaySaga" /> through its generated
///     orchestrator and the repository the generated <c>AddPragmaticSagas</c> registers for <see cref="StaysBoundary" /> (its keyed session),
///     over the tables the generated schema has (EF Core's <c>__SagaInstances</c> and <c>__SagaSteps</c>, compared column for column by
///     <c>TheSqlEnginesSchemaIsPragmaticsTests</c>). On MySQL and Oracle the active sagas' unique index is left out (PRAG3038).
/// </summary>
[Collection("pragmatic databases")]
public class SagaStoreTests(PragmaticDatabases databases)
{
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_saga_runs_through_its_steps_and_a_step_delivered_twice_runs_once(SqlDialect dialect)
        => await (await ScenarioAsync(dialect)).A_saga_runs_through_its_steps_and_a_step_delivered_twice_runs_once();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_saga_whose_window_passes_is_compensated_and_timed_out(SqlDialect dialect)
        => await (await ScenarioAsync(dialect)).A_saga_whose_window_passes_is_compensated_and_timed_out();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_saga_whose_step_rejects_is_compensated(SqlDialect dialect)
        => await (await ScenarioAsync(dialect)).A_saga_whose_step_rejects_is_compensated();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_write_from_a_state_another_save_overtook_is_refused(SqlDialect dialect)
        => await (await ScenarioAsync(dialect)).A_write_from_a_state_another_save_overtook_is_refused();

    /// <summary>
    ///     The registration resolves the engine's repository, not the in-memory one: the scenario would pass on the in-memory store too, and
    ///     would then prove nothing about the database.
    /// </summary>
    [Fact]
    public async Task The_generated_registration_adds_the_engines_repository()
    {
        using var session = await databases.SessionAsync(SqlDialect.Sqlite, null);
        await using var provider = Host(session, new SagaBus());
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<SqlSagaRepository<StaySaga, StayState>>(scope.ServiceProvider.GetRequiredService<ISagaRepository<StaySaga>>());
    }

    private async Task<SagaScenario> ScenarioAsync(SqlDialect dialect)
    {
        var session = await databases.SessionAsync(dialect, null);
        var bus = new SagaBus();
        var provider = Host(session, bus);
        return new SagaScenario(provider, bus,
            async id => await session.Query<SagaInstanceRow>().FirstOrDefaultAsync((r, i) => r.Id == i, id) is { } row
                ? ((SagaStatus)row.Status, row.TimeoutAt is not null)
                : throw new InvalidOperationException($"No saga instance {id}"),
            async id => (await session.Query<SagaStepRow>().Where((s, i) => s.SagaInstanceId == i, id).ToListAsync()).Count,
            async correlation => (await session.Query<Pragmatic.Messaging.Engines.Sql.OutboxMessageRow>().Where((m, c) => m.CorrelationId == c, correlation).ToListAsync()).Count);
    }

    /// <summary>The application: the boundary's session (an instance: a scope does not dispose the test's), the bus, and the generated registration.</summary>
    private static ServiceProvider Host(SqlSession session, SagaBus bus)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<SqlSession>(typeof(StaysBoundary), session);
        services.AddSingleton<IMessageBus>(bus);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddPragmaticSagas();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
