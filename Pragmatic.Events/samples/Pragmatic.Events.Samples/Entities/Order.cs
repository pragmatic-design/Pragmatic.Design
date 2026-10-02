using Pragmatic.Events.Samples.Events;

namespace Pragmatic.Events.Samples.Entities;

/// <summary>
///     Sample order entity that raises domain events.
///     Uses TimeProvider for testable timestamps.
/// </summary>
public sealed class Order(TimeProvider? timeProvider = null) : DomainEventSource
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Guid Id { get; } = Guid.NewGuid();
    public string Product { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public bool IsCancelled { get; private set; }

    public void Place(string product, int quantity)
    {
        Product = product;
        Quantity = quantity;
        RaiseEvent(new OrderPlacedEvent(Id, product, quantity, _timeProvider.GetUtcNow()));
    }

    public void Cancel(string reason)
    {
        IsCancelled = true;
        RaiseEvent(new OrderCancelledEvent(Id, reason, _timeProvider.GetUtcNow()));
    }
}
