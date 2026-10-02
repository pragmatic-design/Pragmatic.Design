using Pragmatic.Events.Samples.Events;

namespace Pragmatic.Events.Samples.Handlers;

/// <summary>
///     Sends a confirmation email when an order is placed.
/// </summary>
public sealed class SendConfirmationHandler : IDomainEventHandler<OrderPlacedEvent>
{
    public Task HandleAsync(OrderPlacedEvent @event, CancellationToken ct = default)
    {
        Console.WriteLine($"  [Handler] Sending confirmation email for order {@event.OrderId}");
        return Task.CompletedTask;
    }
}
