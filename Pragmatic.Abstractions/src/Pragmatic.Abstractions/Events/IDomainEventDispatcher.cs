namespace Pragmatic.Events;

/// <summary>
///     Dispatches domain events to their handlers.
/// </summary>
/// <remarks>
///     <para>
///         The dispatcher is responsible for finding and invoking all handlers
///         registered for a given event type. Implementations may dispatch events:
///     </para>
///     <list type="bullet">
///         <item>Synchronously (in-memory, same transaction)</item>
///         <item>Asynchronously (via message queue, different transaction)</item>
///         <item>Both (immediate handlers + outbox for reliability)</item>
///     </list>
/// </remarks>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface IDomainEventDispatcher
{
    /// <summary>
    ///     Dispatches a single event to all registered handlers.
    /// </summary>
    /// <typeparam name="TEvent">The type of event.</typeparam>
    /// <param name="event">The event to dispatch.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IDomainEvent;

    /// <summary>
    ///     Dispatches multiple events to their handlers.
    ///     Events are dispatched in order.
    /// </summary>
    /// <param name="events">
    ///     The events to dispatch. Implementations must not enumerate this sequence more than
    ///     once — buffer it (e.g. <c>ToList()</c>) internally if a count or multiple passes
    ///     are required. Callers should pass materialised collections (arrays, lists) to avoid
    ///     unintended deferred execution.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default);
}
