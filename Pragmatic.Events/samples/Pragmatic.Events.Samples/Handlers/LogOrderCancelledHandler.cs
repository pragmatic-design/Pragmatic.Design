using Pragmatic.Events.Samples.Events;

namespace Pragmatic.Events.Samples.Handlers;

/// <summary>
///     Logs when an order is cancelled.
/// </summary>
public sealed class LogOrderCancelledHandler : IDomainEventHandler<OrderCancelledEvent>
{
    public Task HandleAsync(OrderCancelledEvent @event, CancellationToken ct = default)
    {
        Console.WriteLine($"  [Handler] Order {@event.OrderId} cancelled: {@event.Reason}");
        return Task.CompletedTask;
    }
}
