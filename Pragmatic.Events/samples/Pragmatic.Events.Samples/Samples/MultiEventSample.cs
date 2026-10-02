using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events.Extensions;
using Pragmatic.Events.Samples.Entities;
using Pragmatic.Events.Samples.Events;
using Pragmatic.Events.Samples.Handlers;

namespace Pragmatic.Events.Samples.Samples;

/// <summary>
///     Multi-event scenarios: entity accumulates multiple events before dispatch,
///     and batch dispatch processes all events in sequence.
/// </summary>
public static class MultiEventSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("4. Multi-Event — Batch dispatch and event accumulation");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryDomainEvents();
        services.AddDomainEventHandler<LogOrderPlacedHandler, OrderPlacedEvent>();
        services.AddDomainEventHandler<LogOrderCancelledHandler, OrderCancelledEvent>();
        services.AddDomainEventHandler<LogPaymentHandler, PaymentProcessedEvent>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        Console.WriteLine("  4.1 Entity accumulates multiple events");
        Console.WriteLine("  -----------------------------------------");

        var order = new Order();
        order.Place("Widget Pro", 5);
        order.Cancel("Out of stock");

        Console.WriteLine($"    Events pending: {order.DomainEvents.Count} (OrderPlaced + OrderCancelled)");
        Console.WriteLine("    Dispatching all at once:");
        await dispatcher.DispatchAsync(order.DomainEvents);
        order.ClearDomainEvents();
        Console.WriteLine();

        Console.WriteLine("  4.2 Mixed event types in single batch");
        Console.WriteLine("  ----------------------------------------");

        // Dispatch a mix of different event types
        var events = new IDomainEvent[]
        {
            new OrderPlacedEvent(Guid.NewGuid(), "Item A", 1, DateTimeOffset.UtcNow),
            new PaymentProcessedEvent(Guid.NewGuid(), 49.99m, "EUR", DateTimeOffset.UtcNow),
            new OrderCancelledEvent(Guid.NewGuid(), "Refund requested", DateTimeOffset.UtcNow)
        };

        Console.WriteLine($"    Dispatching {events.Length} mixed events:");
        await dispatcher.DispatchAsync(events);
        Console.WriteLine();
    }
}
