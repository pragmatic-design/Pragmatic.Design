using Pragmatic.Messaging.Saga;

namespace Warehouse.IntegrationTests.Infrastructure;

/// <summary>
///     The host's saga repository, unchanged except that saving after <paramref name="failingStep" /> throws.
/// </summary>
/// <remarks>
///     A step of a saga in this example does not fail on its own: each one sets a field and returns a
///     message. The save that follows it runs inside the orchestrator's catch for that step, so a fault
///     here is a step failure as the orchestrator sees one, without changing what the saga does.
/// </remarks>
internal sealed class RepositoryThatFailsOneStep<TSaga>(ISagaRepository<TSaga> inner, string failingStep)
    : ISagaRepository<TSaga>
    where TSaga : class
{
    public Task<TSaga?> FindByCorrelationAsync(string correlationId, CancellationToken ct = default)
        => inner.FindByCorrelationAsync(correlationId, ct);

    public Task<TSaga?> FindByIdAsync(Guid id, CancellationToken ct = default) => inner.FindByIdAsync(id, ct);

    public Task SaveAsync(TSaga saga, CancellationToken ct = default) => inner.SaveAsync(saga, ct);

    public Task SaveWithTimeoutAsync(TSaga saga, DateTimeOffset? timeoutAt, CancellationToken ct = default)
        => inner.SaveWithTimeoutAsync(saga, timeoutAt, ct);

    public Task SaveWithStepAsync(TSaga saga, DateTimeOffset? timeoutAt, string executedStepName, CancellationToken ct = default)
        => executedStepName == failingStep
            ? throw Injected(executedStepName)
            : inner.SaveWithStepAsync(saga, timeoutAt, executedStepName, ct);

    public Task<bool> SaveWithStepAndOutboxAsync(
        TSaga saga, DateTimeOffset? timeoutAt, string executedStepName,
        IReadOnlyList<object> pendingMessages, CancellationToken ct = default)
        => executedStepName == failingStep
            ? throw Injected(executedStepName)
            : inner.SaveWithStepAndOutboxAsync(saga, timeoutAt, executedStepName, pendingMessages, ct);

    public Task<IReadOnlyList<string>> GetExecutedStepNamesAsync(Guid sagaId, CancellationToken ct = default)
        => inner.GetExecutedStepNamesAsync(sagaId, ct);

    public Task<IReadOnlyList<TSaga>> GetActiveAsync(CancellationToken ct = default) => inner.GetActiveAsync(ct);

    public Task SetTimeoutAsync(Guid sagaId, DateTimeOffset? timeoutAt, CancellationToken ct = default)
        => inner.SetTimeoutAsync(sagaId, timeoutAt, ct);

    public Task<IReadOnlyList<TSaga>> GetDueForTimeoutAsync(DateTimeOffset asOf, CancellationToken ct = default)
        => inner.GetDueForTimeoutAsync(asOf, ct);

    public Task MarkTimedOutAsync(Guid sagaId, CancellationToken ct = default) => inner.MarkTimedOutAsync(sagaId, ct);

    public Task MarkCompensatedAsync(Guid sagaId, CancellationToken ct = default) => inner.MarkCompensatedAsync(sagaId, ct);

    public Task MarkFaultedAsync(Guid sagaId, string error, CancellationToken ct = default)
        => inner.MarkFaultedAsync(sagaId, error, ct);

    private static InvalidOperationException Injected(string step)
        => new($"Injected by the suite: the save after {step} fails.");
}
