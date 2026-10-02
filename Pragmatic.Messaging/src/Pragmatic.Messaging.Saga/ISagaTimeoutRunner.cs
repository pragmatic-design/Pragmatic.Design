namespace Pragmatic.Messaging.Saga;

/// <summary>
///     Type-erased entry point used by <see cref="SagaTimeoutBackgroundService"/>
///     to drive timed-out sagas of a specific type through their compensation /
///     terminal handling. One implementation per saga is SG-generated next to the
///     orchestrator and registered in DI via <c>AddPragmaticSagas()</c>.
/// </summary>
public interface ISagaTimeoutRunner
{
    /// <summary>
    ///     Loads sagas whose <c>TimeoutAt</c> is already in the past and invokes
    ///     their orchestrator's timeout handler (compensation chain + terminal
    ///     state transition). Exceptions on individual sagas MUST be logged and
    ///     swallowed — the background loop keeps going.
    /// </summary>
    Task RunDueTimeoutsAsync(DateTimeOffset asOf, CancellationToken ct);
}
