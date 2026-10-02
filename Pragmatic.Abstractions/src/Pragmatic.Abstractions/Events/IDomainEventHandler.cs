namespace Pragmatic.Events;

/// <summary>
///     Handles a specific type of domain event.
/// </summary>
/// <typeparam name="TEvent">The type of event this handler processes.</typeparam>
/// <remarks>
///     <para>
///         Handlers are responsible for reacting to domain events. Common uses include:
///     </para>
///     <list type="bullet">
///         <item>Sending notifications (email, SMS, push)</item>
///         <item>Updating read models or projections</item>
///         <item>Triggering workflows or sagas</item>
///         <item>Publishing to external systems</item>
///     </list>
///     <para>
///         Multiple handlers can be registered for the same event type.
///         Handlers are executed in ascending <see cref="Order" /> (lower values run first).
///         Handlers with the same order execute in DI registration order.
///     </para>
/// </remarks>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>
    ///     Execution order for this handler. Lower values execute first.
    ///     Default is 0.
    /// </summary>
    int Order => 0;

    /// <summary>
    ///     Handles the domain event.
    /// </summary>
    /// <param name="event">The event to handle.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    ///     <para>
    ///         <b>Error isolation:</b> handlers are isolated from one another. If this method
    ///         throws, the in-memory dispatcher logs the failure (and records a diagnostic) but
    ///         <b>continues invoking the remaining handlers</b> — one failing handler does not
    ///         abort the dispatch chain. The only exception is <see cref="OperationCanceledException"/>
    ///         (raised by honouring <paramref name="ct"/>), which propagates and aborts the chain.
    ///     </para>
    ///     <para>
    ///         Consequently a handler must not rely on a sibling handler having succeeded, and must
    ///         keep its own side effects idempotent/compensatable. Use the outbox/transactional
    ///         dispatch path when at-least-once delivery across a transaction boundary is required.
    ///     </para>
    /// </remarks>
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}
