using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Messaging.Extensions;

namespace Pragmatic.Messaging.Outbox.Samples;

/// <summary>
///     Choreography: no orchestrator — plain handlers reacting to each other's facts.
///     Ordering publishes <see cref="OrderPlaced" />; Billing charges and publishes either
///     <see cref="PaymentCollected" /> or the compensating fact
///     <see cref="OrderPaymentFailed" />; Shipping and Ordering react independently.
///     <c>[CorrelationKey]</c> marks the business key so every participant (and any saga
///     consuming the same events) correlates without implementing interfaces.
///     Compare with the orchestrated version in the saga guide: same flow, inverted
///     ownership.
/// </summary>
public static class ChoreographySample
{
    public sealed record OrderPlaced([property: CorrelationKey] Guid OrderId, decimal Amount);
    public sealed record PaymentCollected([property: CorrelationKey] Guid OrderId);
    public sealed record OrderPaymentFailed([property: CorrelationKey] Guid OrderId, string Reason);

    /// <summary>Billing: reacts to the order fact, emits the payment fact (or its compensation).</summary>
    public sealed class ChargeOnOrderPlaced(IMessageBus bus) : IMessageHandler<OrderPlaced>
    {
        public async Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct)
        {
            // Charge succeeds under 1000, fails above — stands in for a real gateway.
            if (message.Amount < 1000m)
            {
                Console.WriteLine($"  [billing]  charged {message.Amount:C} for order {message.OrderId:N}");
                await bus.PublishAsync(new PaymentCollected(message.OrderId), context, ct);
            }
            else
            {
                Console.WriteLine($"  [billing]  charge DECLINED for order {message.OrderId:N}");
                await bus.PublishAsync(new OrderPaymentFailed(message.OrderId, "limit exceeded"), context, ct);
            }
        }
    }

    /// <summary>Shipping: knows only its trigger, not the flow.</summary>
    public sealed class ShipOnPaymentCollected : IMessageHandler<PaymentCollected>
    {
        public Task HandleAsync(PaymentCollected message, MessageContext context, CancellationToken ct)
        {
            Console.WriteLine($"  [shipping] shipment created for order {message.OrderId:N}");
            return Task.CompletedTask;
        }
    }

    /// <summary>Ordering: compensates ITS OWN state when the payment fact says so.</summary>
    public sealed class CancelOnPaymentFailed : IMessageHandler<OrderPaymentFailed>
    {
        public Task HandleAsync(OrderPaymentFailed message, MessageContext context, CancellationToken ct)
        {
            Console.WriteLine($"  [ordering] order {message.OrderId:N} cancelled ({message.Reason})");
            return Task.CompletedTask;
        }
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("--- Choreography (handlers + events, no orchestrator) ---");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging();
        services.AddScoped<IMessageHandler<OrderPlaced>, ChargeOnOrderPlaced>();
        services.AddScoped<IMessageHandler<PaymentCollected>, ShipOnPaymentCollected>();
        services.AddScoped<IMessageHandler<OrderPaymentFailed>, CancelOnPaymentFailed>();

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            using var scope = provider.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            // Happy path: OrderPlaced → PaymentCollected → shipment.
            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 180m));

            // Compensation path: OrderPlaced → OrderPaymentFailed → cancel.
            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 5_000m));
        }

        Console.WriteLine();
    }
}
