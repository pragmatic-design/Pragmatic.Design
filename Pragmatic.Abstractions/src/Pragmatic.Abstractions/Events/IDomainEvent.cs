namespace Pragmatic.Events;

/// <summary>
///     Marker interface for domain events.
///     Domain events represent something significant that happened in the domain.
/// </summary>
/// <remarks>
///     <para>
///         Domain events are used to:
///     </para>
///     <list type="bullet">
///         <item>Decouple aggregates by communicating changes without direct references</item>
///         <item>Trigger side effects (sending emails, updating read models, etc.)</item>
///         <item>Enable event sourcing patterns</item>
///         <item>Audit and track business-relevant occurrences</item>
///     </list>
///     <para>
///         Events should be immutable records that capture the essential facts
///         about what happened, including any relevant identifiers and timestamps.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public sealed record OrderPlaced(
///     Guid OrderId,
///     Guid CustomerId,
///     decimal TotalAmount,
///     DateTimeOffset OccurredAt) : IDomainEvent;
/// </code>
/// </example>
public interface IDomainEvent
{
    /// <summary>
    ///     When this event occurred.
    /// </summary>
    DateTimeOffset OccurredAt { get; }

    /// <summary>
    ///     Unique identity of this event instance, used for deduplication, idempotent
    ///     dispatch, and tracing.
    /// </summary>
    /// <remarks>
    ///     Default implementation returns <see cref="Guid.Empty"/> so existing events that
    ///     do not declare an <c>EventId</c> keep compiling. Events that need a real unique
    ///     id should derive from <c>DomainEvent</c> (which assigns a fresh GUID per instance)
    ///     or override this property with their own initialized value.
    /// </remarks>
    Guid EventId => Guid.Empty;
}
