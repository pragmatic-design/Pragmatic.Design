using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Events;

/// <summary>
///     Base class providing domain event support for entities.
///     Inherit from this class to enable domain event raising.
/// </summary>
/// <remarks>
///     <para>
///         This class provides a simple implementation of <see cref="IHasDomainEvents" />
///         that can be used by entities. Events are accumulated and should be
///         dispatched after successful persistence.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Entity]
/// public partial class Order : DomainEventSource
/// {
///     private readonly TimeProvider _timeProvider;
///
///     public Order(TimeProvider? timeProvider = null)
///     {
///         _timeProvider = timeProvider ?? TimeProvider.System;
///     }
///
///     public void Place(Guid customerId, decimal total)
///     {
///         // Use TimeProvider for testable timestamps
///         RaiseEvent(new OrderPlaced(Id, customerId, total, _timeProvider.GetUtcNow()));
///     }
/// }
/// </code>
/// </example>
public abstract class DomainEventSource : IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <inheritdoc />
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <inheritdoc />
    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    /// <summary>
    ///     Raises a domain event. The event will be accumulated until dispatched.
    /// </summary>
    /// <param name="event">The event to raise.</param>
    protected void RaiseEvent(IDomainEvent @event)
    {
        ThrowIfNull(@event);
        _domainEvents.Add(@event);
    }

    /// <summary>
    ///     Raises multiple domain events.
    /// </summary>
    /// <param name="events">The events to raise. No element may be null.</param>
    protected void RaiseEvents(IEnumerable<IDomainEvent> events)
    {
        ThrowIfNull(events);
        foreach (var @event in events)
        {
            ThrowIfNull(@event);
            _domainEvents.Add(@event);
        }
    }
}
