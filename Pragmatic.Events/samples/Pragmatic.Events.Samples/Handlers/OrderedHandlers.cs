using Pragmatic.Events.Samples.Events;

namespace Pragmatic.Events.Samples.Handlers;

/// <summary>
///     First handler (Order=10) — runs first for OrderPlacedEvent.
/// </summary>
public sealed class AuditOrderHandler : IDomainEventHandler<OrderPlacedEvent>
{
    public int Order => 10;

    public Task HandleAsync(OrderPlacedEvent @event, CancellationToken ct = default)
    {
        Console.WriteLine($"    [1st - Audit, Order=10]    Order {@event.OrderId:N} audited");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Second handler (Order=20) — runs after audit.
/// </summary>
public sealed class InventoryReserveHandler : IDomainEventHandler<OrderPlacedEvent>
{
    public int Order => 20;

    public Task HandleAsync(OrderPlacedEvent @event, CancellationToken ct = default)
    {
        Console.WriteLine($"    [2nd - Inventory, Order=20] Reserved {Quantity(@event)} items");
        return Task.CompletedTask;
    }

    private static int Quantity(OrderPlacedEvent e) => e.Quantity;
}

/// <summary>
///     Third handler (Order=30) — runs last, sends notification.
/// </summary>
public sealed class NotifyOrderHandler : IDomainEventHandler<OrderPlacedEvent>
{
    public int Order => 30;

    public Task HandleAsync(OrderPlacedEvent @event, CancellationToken ct = default)
    {
        Console.WriteLine($"    [3rd - Notify, Order=30]   Notification sent for {@event.Product}");
        return Task.CompletedTask;
    }
}
