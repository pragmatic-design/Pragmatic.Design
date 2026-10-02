using Pragmatic.Events.Samples.Events;

namespace Pragmatic.Events.Samples.Handlers;

/// <summary>
///     Handler that deliberately throws — demonstrates dispatch resilience.
///     The dispatcher continues to the next handler even when one fails.
/// </summary>
public sealed class FailingPaymentHandler : IDomainEventHandler<PaymentProcessedEvent>
{
    public int Order => 10;

    public Task HandleAsync(PaymentProcessedEvent @event, CancellationToken ct = default)
    {
        Console.WriteLine($"    [FailingHandler] About to throw for payment {@event.Amount:C}...");
        throw new InvalidOperationException("Payment gateway timeout (simulated failure)");
    }
}

/// <summary>
///     Handler that runs AFTER the failing one — proves resilience.
/// </summary>
public sealed class LogPaymentHandler : IDomainEventHandler<PaymentProcessedEvent>
{
    public int Order => 20;

    public Task HandleAsync(PaymentProcessedEvent @event, CancellationToken ct = default)
    {
        Console.WriteLine($"    [LogPayment, Order=20] Payment of {@event.Amount:C} {@event.Currency} logged (still runs!)");
        return Task.CompletedTask;
    }
}
