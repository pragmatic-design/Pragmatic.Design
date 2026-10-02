using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events.Extensions;
using Pragmatic.Events.Samples.Events;
using Pragmatic.Events.Samples.Handlers;

namespace Pragmatic.Events.Samples.Samples;

/// <summary>
///     Demonstrates dispatcher resilience: when a handler throws,
///     the remaining handlers still execute.
/// </summary>
public static class ErrorResilienceSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("3. Error Resilience — Dispatch continues on handler failure");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryDomainEvents();
        services.AddDomainEventHandler<FailingPaymentHandler, PaymentProcessedEvent>();
        services.AddDomainEventHandler<LogPaymentHandler, PaymentProcessedEvent>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        Console.WriteLine("  FailingPaymentHandler (Order=10) throws InvalidOperationException");
        Console.WriteLine("  LogPaymentHandler     (Order=20) should STILL execute");
        Console.WriteLine();

        var payment = new PaymentProcessedEvent(
            Guid.NewGuid(), 99.99m, "EUR", DateTimeOffset.UtcNow);

        Console.WriteLine("  Dispatching PaymentProcessedEvent:");
        await dispatcher.DispatchAsync(payment);
        Console.WriteLine();

        Console.WriteLine("  The LogPaymentHandler ran despite the failure above.");
        Console.WriteLine("  Failures are logged but don't break the event chain.");
        Console.WriteLine();
    }
}
