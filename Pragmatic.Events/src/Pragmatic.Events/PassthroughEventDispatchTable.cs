namespace Pragmatic.Events;

/// <summary>
///     Default passthrough implementation of <see cref="ITypedEventDispatchTable" />.
///     Returns null for all event types, indicating no compile-time dispatch is available.
///     The SG-generated implementation replaces this when available.
/// </summary>
internal sealed class PassthroughEventDispatchTable : ITypedEventDispatchTable
{
    public Task? TryDispatch(IDomainEventDispatcher dispatcher, IDomainEvent @event, CancellationToken ct)
        => null;
}
