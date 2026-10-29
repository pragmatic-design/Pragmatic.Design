namespace Pragmatic.Events.Engines;

/// <summary>
///     One delivery pass of a boundary's event outbox on an engine, on demand: what <see cref="EngineEventOutboxDeliveryService{TBoundary}" />
///     runs on a timer, and what an application calls to deliver without waiting for it (enqueue, drain, read the effect).
/// </summary>
/// <remarks>
///     Generic on the boundary, as EF Core's drainer is on its context: two boundaries with an outbox are two registrations, and the caller names
///     which one to drain. Not a substitute for the loop: an event nobody drains is written down and never delivered.
/// </remarks>
/// <typeparam name="TBoundary">The boundary whose outbox is drained: its session, by its key, is the one the rows are read through.</typeparam>
public interface IEngineEventOutboxDrainer<TBoundary>
{
    /// <summary>
    ///     Claims a batch of the pending rows, dispatches each, and marks what was delivered. One batch, the rows eligible when it started: a
    ///     caller that needs all of N events delivered drains until the return is 0. A handler that throws does not fail the pass: its row keeps
    ///     its error and is retried by a later one.
    /// </summary>
    /// <returns>How many rows this pass delivered.</returns>
    Task<int> DrainOnceAsync(CancellationToken ct = default);
}
