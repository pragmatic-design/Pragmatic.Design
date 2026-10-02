using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events.Extensions;
using Pragmatic.Events.Samples.Entities;
using Pragmatic.Events.Samples.Events;
using Pragmatic.Events.Samples.Handlers;

namespace Pragmatic.Events.Samples.Samples;

/// <summary>
///     TimeProvider integration for testable timestamps in domain events.
/// </summary>
public static class TimeProviderSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("6. TimeProvider — Testable Event Timestamps");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  6.1 Default TimeProvider — uses system clock");
        Console.WriteLine("  ------------------------------------------------");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryDomainEvents();
        services.AddDomainEventHandler<TimestampLogHandler, OrderPlacedEvent>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        // Default: TimeProvider.System
        var order1 = new Order();
        order1.Place("Widget", 1);
        await dispatcher.DispatchAsync(order1.DomainEvents);
        Console.WriteLine();

        Console.WriteLine("  6.2 Fake TimeProvider — deterministic timestamps for tests");
        Console.WriteLine("  -------------------------------------------------------------");

        // In tests, inject a fake TimeProvider for deterministic timestamps
        var fakeTime = new DateTimeOffset(2025, 12, 25, 10, 0, 0, TimeSpan.Zero);
        var fakeProvider = new FakeTimeProvider(fakeTime);
        var order2 = new Order(fakeProvider);
        order2.Place("Test Product", 3);
        await dispatcher.DispatchAsync(order2.DomainEvents);

        Console.WriteLine();
        Console.WriteLine("    Fake TimeProvider ensures tests are deterministic —");
        Console.WriteLine("    events always have the same OccurredAt timestamp.");
        Console.WriteLine();
    }
}

/// <summary>
///     Handler that displays the event timestamp.
/// </summary>
public sealed class TimestampLogHandler : IDomainEventHandler<OrderPlacedEvent>
{
    public Task HandleAsync(OrderPlacedEvent @event, CancellationToken ct = default)
    {
        Console.WriteLine($"    Event: {@event.Product} x{@event.Quantity} at {@event.OccurredAt:yyyy-MM-dd HH:mm:ss zzz}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Simple fake TimeProvider for testing.
/// </summary>
public sealed class FakeTimeProvider(DateTimeOffset fixedTime) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => fixedTime;
}
