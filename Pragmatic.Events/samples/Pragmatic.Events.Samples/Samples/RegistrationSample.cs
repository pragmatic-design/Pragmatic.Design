using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events.Extensions;
using Pragmatic.Events.Samples.Entities;
using Pragmatic.Events.Samples.Events;
using Pragmatic.Events.Samples.Handlers;

namespace Pragmatic.Events.Samples.Samples;

/// <summary>
///     Three ways to register event handlers: manual, assembly scanning, SG-generated.
/// </summary>
public static class RegistrationSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("5. Handler Registration — 3 Strategies");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        await ShowManualRegistration();
        ShowSgGenerated();

        Console.WriteLine();
    }

    private static async Task ShowManualRegistration()
    {
        Console.WriteLine("  5.1 Manual — explicit handler + event type");
        Console.WriteLine("  ----------------------------------------------");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryDomainEvents();
        services.AddDomainEventHandler<LogOrderPlacedHandler, OrderPlacedEvent>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        var order = new Order();
        order.Place("Widget", 1);
        await dispatcher.DispatchAsync(order.DomainEvents);

        Console.WriteLine("    AddDomainEventHandler<THandler, TEvent>() — type-safe, explicit");
        Console.WriteLine();
    }

    private static void ShowSgGenerated()
    {
        Console.WriteLine("  5.3 SG-generated — [EventHandler] attribute (AOT-safe)");
        Console.WriteLine("  ---------------------------------------------------------");

        Console.WriteLine("""
            // Mark handler with [EventHandler]:
            [EventHandler]
            public class MyHandler : IDomainEventHandler<MyEvent> { ... }

            // SG generates:
            services.AddPragmaticEventHandlers();  // Zero reflection, AOT-safe
        """);
        Console.WriteLine();
    }
}
