namespace Pragmatic.Messaging.Saga;

/// <summary>
///     Persistence for saga state. The SG-generated orchestrator uses this
///     to load/save saga instances.
/// </summary>
/// <typeparam name="TSaga">The concrete saga type.</typeparam>
public interface ISagaRepository<TSaga> where TSaga : class
{
    /// <summary>Finds a saga by its correlation ID (e.g., OrderId).</summary>
    Task<TSaga?> FindByCorrelationAsync(string correlationId, CancellationToken ct = default);

    /// <summary>Finds a saga by its unique ID.</summary>
    Task<TSaga?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Saves (insert or update) a saga instance.</summary>
    Task SaveAsync(TSaga saga, CancellationToken ct = default);

    /// <summary>
    ///     Saves the saga AND its next step deadline (<paramref name="timeoutAt"/>, null clears it) in a
    ///     SINGLE atomic commit. The generated orchestrator uses this after every transition so state and
    ///     deadline can never diverge: as two writes, <see cref="SaveAsync"/> then
    ///     <see cref="SetTimeoutAsync"/>, a crash between them would leave a saga with no deadline that
    ///     the timeout scanner never picks up.
    ///     <para>
    ///     The default implementation is the non-atomic <see cref="SaveAsync"/> only (a lightweight
    ///     repository that does not track deadlines); durable repositories override it to write state and
    ///     deadline in one transaction.
    ///     </para>
    /// </summary>
    Task SaveWithTimeoutAsync(TSaga saga, DateTimeOffset? timeoutAt, CancellationToken ct = default)
        => SaveAsync(saga, ct);

    /// <summary>
    ///     Records <paramref name="executedStepName"/> in the saga's step history (<c>__SagaSteps</c>) AND
    ///     saves state + deadline in a SINGLE atomic commit. Populates the history the docs
    ///     advertise and that path-based compensation reads. The default ignores the step name and
    ///     delegates to <see cref="SaveWithTimeoutAsync"/> — a lightweight repository that keeps no history.
    /// </summary>
    Task SaveWithStepAsync(TSaga saga, DateTimeOffset? timeoutAt, string executedStepName, CancellationToken ct = default)
        => SaveWithTimeoutAsync(saga, timeoutAt, ct);

    /// <summary>
    ///     Saves state + step (as <see cref="SaveWithStepAsync"/>) and, when the backing store provides a
    ///     transactional outbox, writes the step's resulting <paramref name="pendingMessages"/> into it in
    ///     the SAME transaction — closing the save-before-publish dual-write window: the action cannot
    ///     be lost if the transport fails after the state commit (exactly-once via the outbox pump).
    ///     Returns <c>true</c> when the messages were persisted to an outbox (the caller must NOT publish
    ///     them); <c>false</c> when the store has no outbox (the caller publishes them inline,
    ///     save-before-publish). The default keeps no outbox: it saves and returns false.
    /// </summary>
    Task<bool> SaveWithStepAndOutboxAsync(
        TSaga saga, DateTimeOffset? timeoutAt, string executedStepName,
        IReadOnlyList<object> pendingMessages, CancellationToken ct = default)
    {
        return SaveThenReportInline(this, saga, timeoutAt, executedStepName, ct);

        static async Task<bool> SaveThenReportInline(
            ISagaRepository<TSaga> repo, TSaga s, DateTimeOffset? t, string step, CancellationToken c)
        {
            await repo.SaveWithStepAsync(s, t, step, c).ConfigureAwait(false);
            return false;
        }
    }

    /// <summary>
    ///     Names of the steps that ACTUALLY executed for the saga, so the generated orchestrator compensates
    ///     only steps that really ran instead of inferring them from declaration order. The default
    ///     returns an empty list (no history); the orchestrator then falls back to the declaration-order chain.
    /// </summary>
    Task<IReadOnlyList<string>> GetExecutedStepNamesAsync(Guid sagaId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    /// <summary>Gets all active (non-completed) saga instances.</summary>
    Task<IReadOnlyList<TSaga>> GetActiveAsync(CancellationToken ct = default);

    /// <summary>
    ///     Sets (or clears, when <paramref name="timeoutAt"/> is null) the deadline
    ///     after which the saga step should be considered timed out. Used by the
    ///     SG-generated orchestrator after each step transition.
    /// </summary>
    Task SetTimeoutAsync(Guid sagaId, DateTimeOffset? timeoutAt, CancellationToken ct = default);

    /// <summary>
    ///     Returns every active saga whose <c>TimeoutAt</c> is set and already in the
    ///     past relative to <paramref name="asOf"/>. Polled by the saga-timeout
    ///     background service to drive compensation on expired steps.
    /// </summary>
    Task<IReadOnlyList<TSaga>> GetDueForTimeoutAsync(DateTimeOffset asOf, CancellationToken ct = default);

    /// <summary>
    ///     Marks the saga as <c>TimedOut</c> (terminal). Called by the orchestrator's
    ///     timeout handler after the compensation chain has run. Clears <c>TimeoutAt</c>
    ///     so the background scanner won't pick the instance up again.
    /// </summary>
    Task MarkTimedOutAsync(Guid sagaId, CancellationToken ct = default);

    /// <summary>
    ///     Marks the saga as <see cref="SagaStatus.Compensated"/> (terminal). Called by the generated
    ///     orchestrator after a step rejected the message with <see cref="SagaRejectedException"/> and
    ///     the compensation chain ran — the saga stops here (no retry). The default is a no-op so
    ///     lightweight repositories can ignore lifecycle status.
    /// </summary>
    Task MarkCompensatedAsync(Guid sagaId, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    ///     Marks the saga as <see cref="SagaStatus.Faulted"/> (terminal for status purposes) with the
    ///     failure reason. Called by the generated orchestrator when a step threw a non-rejection
    ///     exception (the message is still rethrown for the delivery pipeline to retry/dead-letter, but
    ///     the instance no longer reports as healthy/Active). The default is a no-op.
    /// </summary>
    Task MarkFaultedAsync(Guid sagaId, string error, CancellationToken ct = default) => Task.CompletedTask;
}
