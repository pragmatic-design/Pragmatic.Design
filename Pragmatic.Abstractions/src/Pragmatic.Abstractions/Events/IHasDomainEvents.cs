namespace Pragmatic.Events;

/// <summary>
///     Indicates that an entity can raise domain events.
/// </summary>
/// <remarks>
///     <para>
///         Entities implementing this interface can accumulate domain events
///         during their lifecycle. Events are typically dispatched after
///         the entity is persisted (after SaveChanges).
///     </para>
///     <para>
///         This replaces the <c>DomainEventSource</c> base class to allow entities
///         to use their own inheritance hierarchy (e.g., TPH/TPC with EF Core).
///     </para>
/// </remarks>
public interface IHasDomainEvents
{
    /// <summary>
    ///     Gets the domain events that have been raised by this entity.
    /// </summary>
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    /// <summary>
    ///     Clears all domain events. Called after events have been dispatched.
    /// </summary>
    void ClearDomainEvents();
}
