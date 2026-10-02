using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events.Extensions;
using Pragmatic.Events.Samples.Entities;
using Pragmatic.Events.Samples.Events;
using Pragmatic.Events.Samples.Handlers;

namespace Pragmatic.Events.Samples.Samples;

/// <summary>
///     Handler ordering via Order property — lower values execute first.
/// </summary>
public static class HandlerOrderingSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("2. Handler Ordering — Deterministic execution sequence");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  3 handlers with different Order values:");
        Console.WriteLine("    AuditOrderHandler      (Order=10) → runs 1st");
        Console.WriteLine("    InventoryReserveHandler (Order=20) → runs 2nd");
        Console.WriteLine("    NotifyOrderHandler      (Order=30) → runs 3rd");
        Console.WriteLine();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryDomainEvents();

        // Register in any order — dispatcher sorts by Order property
        services.AddDomainEventHandler<NotifyOrderHandler, OrderPlacedEvent>();
        services.AddDomainEventHandler<AuditOrderHandler, OrderPlacedEvent>();
        services.AddDomainEventHandler<InventoryReserveHandler, OrderPlacedEvent>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        var order = new Order();
        order.Place("Premium Gadget", 3);

        Console.WriteLine("  Dispatching (handlers execute in Order sequence):");
        await dispatcher.DispatchAsync(order.DomainEvents);
        order.ClearDomainEvents();
        Console.WriteLine();
    }
}
