using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Engines.Documents;
using Pragmatic.Messaging.Generated;
using Pragmatic.Messaging.Saga;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Schema;
using PragmaticSagas.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     The saga store on the documents engine (Pragmatic.Messaging.Engines), on MongoDB and Cosmos DB: <see cref="StaySaga" /> through its
///     generated orchestrator and the repository the generated <c>AddPragmaticSagas</c> registers for <see cref="StaysBoundary" /> (its keyed
///     session), over the collections the package declares (EF Core's indexes, by name). MongoDB runs a save in one transaction; Cosmos DB as
///     three writes, the instance's conditional update first (<see cref="DocSagaRepository{TSaga, TState}" />).
/// </summary>
[Collection("document servers")]
public class SagaStoreTests(DocumentServers servers)
{
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_saga_runs_through_its_steps_and_a_step_delivered_twice_runs_once(Store store)
        => await (await ScenarioAsync(store)).A_saga_runs_through_its_steps_and_a_step_delivered_twice_runs_once();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_saga_whose_window_passes_is_compensated_and_timed_out(Store store)
        => await (await ScenarioAsync(store)).A_saga_whose_window_passes_is_compensated_and_timed_out();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_saga_whose_step_rejects_is_compensated(Store store)
        => await (await ScenarioAsync(store)).A_saga_whose_step_rejects_is_compensated();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_write_from_a_state_another_save_overtook_is_refused(Store store)
        => await (await ScenarioAsync(store)).A_write_from_a_state_another_save_overtook_is_refused();

    /// <summary>On MongoDB the active sagas' unique index holds: a second active saga for one correlation is refused by the database.</summary>
    [Fact]
    public async Task On_MongoDB_a_second_active_saga_for_one_correlation_is_refused()
    {
        var session = await SessionAsync(Store.Mongo);
        SagaInstanceDocument Active() => new() { Id = Guid.NewGuid(), SagaType = "Stay", CorrelationId = "c-1", Status = 0 };
        await session.InsertAsync(Active());

        var duplicate = await Assert.ThrowsAsync<Pragmatic.Persistence.Documents.Mongo.MongoWriteException>(() => session.InsertAsync(Active()));
        Assert.True(duplicate.IsDuplicateKey);
        await session.InsertAsync(new SagaInstanceDocument { Id = Guid.NewGuid(), SagaType = "Stay", CorrelationId = "c-1", Status = 1 }); // completed: history
    }

    /// <summary>The registration resolves the engine's repository, not the in-memory one, on which the scenario would pass too.</summary>
    [Fact]
    public async Task The_generated_registration_adds_the_engines_repository()
    {
        await using var provider = Host(servers.Session(Store.Mongo), new SagaBus());
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<DocSagaRepository<StaySaga, StayState>>(scope.ServiceProvider.GetRequiredService<ISagaRepository<StaySaga>>());
    }

    private async Task<SagaScenario> ScenarioAsync(Store store)
    {
        var session = await SessionAsync(store);
        var bus = new SagaBus();
        var provider = Host(session, bus);
        return new SagaScenario(provider, bus,
            async id => await session.Query<SagaInstanceDocument>().FirstOrDefaultAsync((d, i) => d.Id == i, id) is { } document
                ? ((SagaStatus)document.Status, document.TimeoutAt is not null)
                : throw new InvalidOperationException($"No saga instance {id}"),
            async id => (await session.Query<SagaStepDocument>().Where((s, i) => s.SagaInstanceId == i, id).ToListAsync()).Count,
            async correlation => (await session.Query<Pragmatic.Messaging.Engines.Documents.OutboxMessageDocument>().Where((m, c) => m.CorrelationId == c, correlation).ToListAsync()).Count);
    }

    /// <summary>A database of its own with the package's three collections, as the boundary's generated schema declares them.</summary>
    private async Task<DocSession> SessionAsync(Store store)
    {
        var session = servers.Session(store);
        await DocSchema.EnsureAsync(session, [SagaInstanceDocument.Schema, SagaStepDocument.Schema, OutboxMessageDocument.Schema]);
        return session;
    }

    /// <summary>The application: the boundary's session, the bus, and the generated registration.</summary>
    private static ServiceProvider Host(DocSession session, SagaBus bus)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<DocSession>(typeof(StaysBoundary), session);
        services.AddSingleton<IMessageBus>(bus);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddPragmaticSagas();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
