namespace Pragmatic.Events;

/// <summary>
///     Base record for domain events with automatic timestamp population.
///     Uses <see cref="TimeProvider" /> when available for testable timestamps.
/// </summary>
/// <remarks>
///     <para>
///         This base record provides a convenient way to create domain events
///         with proper timestamp handling. It accepts an optional <see cref="TimeProvider" />
///         to enable deterministic timestamps in tests.
///     </para>
///     <para>
///         If no <see cref="TimeProvider" /> is provided, falls back to
///         <see cref="DateTimeOffset.UtcNow" /> for backward compatibility.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Define a domain event
/// public sealed record OrderPlaced(
///     Guid OrderId,
///     decimal Total,
///     DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
///
/// // Create with default timestamp (production)
/// var evt = new OrderPlaced(orderId, total, DateTimeOffset.UtcNow);
///
/// // Create with TimeProvider (testable)
/// var evt = new OrderPlaced(orderId, total, timeProvider.GetUtcNow());
/// </code>
/// </example>
public abstract record DomainEvent(DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <summary>
    ///     Unique identity of this event instance, assigned once at construction.
    ///     Used for deduplication, idempotent dispatch, and tracing.
    /// </summary>
    /// <remarks>
    ///     Defaults to a fresh GUID per instance. Pass an explicit value via
    ///     object-initializer syntax (<c>new MyEvent(...) { EventId = ... }</c>) to preserve
    ///     an event's identity across replay or transport.
    /// </remarks>
    public Guid EventId { get; init; } = Guid.NewGuid();
}
