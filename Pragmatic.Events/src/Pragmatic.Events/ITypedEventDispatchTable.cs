namespace Pragmatic.Events;

/// <summary>
///     Source-generated dispatch table that bridges untyped IDomainEvent to typed DispatchAsync&lt;TEvent&gt;
///     via compile-time pattern matching. Eliminates MakeGenericMethod reflection at runtime.
/// </summary>
public interface ITypedEventDispatchTable
{
    /// <summary>
    ///     Attempts to dispatch the event using a compile-time generated switch.
    ///     Returns <see langword="null" /> if the event type is not in the SG-generated table;
    ///     the caller falls back to <c>dynamic</c> dispatch (DLR, not reflection).
    /// </summary>
    Task? TryDispatch(IDomainEventDispatcher dispatcher, IDomainEvent @event, CancellationToken ct);
}
