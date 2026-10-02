using Pragmatic.Events.Samples.Events;

namespace Pragmatic.Events.Samples.Handlers;

/// <summary>
///     Logs when an order is placed.
/// </summary>
public sealed class LogOrderPlacedHandler : IDomainEventHandler<OrderPlacedEvent>
{
    public Task HandleAsync(OrderPlacedEvent @event, CancellationToken ct = default)
    {
        Console.WriteLine($"  [Handler] Order {@event.OrderId} placed: {@event.Product} x{@event.Quantity}");
        return Task.CompletedTask;
    }
}
