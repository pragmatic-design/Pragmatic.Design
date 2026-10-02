using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events.Extensions;
using Pragmatic.Events.Samples.Entities;
using Pragmatic.Events.Samples.Events;
using Pragmatic.Events.Samples.Handlers;

namespace Pragmatic.Events.Samples.Samples;

/// <summary>
///     Core domain events: entity raises events, dispatcher invokes handlers.
/// </summary>
public static class BasicDispatchSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("1. Basic Domain Events — Raise, Dispatch, Handle");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryDomainEvents();
        services.AddDomainEventHandler<LogOrderPlacedHandler, OrderPlacedEvent>();
        services.AddDomainEventHandler<SendConfirmationHandler, OrderPlacedEvent>();
        services.AddDomainEventHandler<LogOrderCancelledHandler, OrderCancelledEvent>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        // Place an order — entity raises OrderPlacedEvent
        Console.WriteLine("  1.1 Place Order — 2 handlers fire");
        Console.WriteLine("  ------------------------------------");
        var order = new Order();
        order.Place("Widget Pro", 5);
        Console.WriteLine($"    Order {order.Id:N} has {order.DomainEvents.Count} pending event(s)");
        await dispatcher.DispatchAsync(order.DomainEvents);
        order.ClearDomainEvents();
        Console.WriteLine();

        // Cancel the order — different event, different handler
        Console.WriteLine("  1.2 Cancel Order — 1 handler fires");
        Console.WriteLine("  ------------------------------------");
        order.Cancel("Customer changed their mind");
        Console.WriteLine($"    Order has {order.DomainEvents.Count} pending event(s)");
        await dispatcher.DispatchAsync(order.DomainEvents);
        order.ClearDomainEvents();
        Console.WriteLine();
    }
}
