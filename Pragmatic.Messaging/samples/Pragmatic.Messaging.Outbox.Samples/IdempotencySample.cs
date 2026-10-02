using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Extensions;

namespace Pragmatic.Messaging.Outbox.Samples;

/// <summary>
///     Idempotent processing: <see cref="IIdempotencyStore.TryMarkAsProcessedAsync" />
///     atomically records a message id and returns <c>true</c> only the first
///     time it is seen. <c>builder.EnableIdempotency()</c> registers the in-memory
///     store; an idempotency middleware gates the handler on the message id so a
///     redelivered message is recognised and skipped — at-most-once handling.
/// </summary>
public static class IdempotencySample
{
    public sealed record PaymentCaptured(Guid PaymentId, decimal Amount);

    public sealed class LedgerHandler : IMessageHandler<PaymentCaptured>
    {
        public decimal Balance { get; private set; }
        public int Applied { get; private set; }

        public Task HandleAsync(PaymentCaptured message, MessageContext context, CancellationToken ct)
        {
            Balance += message.Amount;
            Applied++;
            return Task.CompletedTask;
        }
    }

    /// <summary>Skips the handler if the message id was already processed.</summary>
    public sealed class IdempotencyMiddleware(IIdempotencyStore store) : IMessageMiddleware
    {
        public int Order => -10;

        public async Task InvokeAsync<T>(T message, MessageContext context, MessageHandlerDelegate next, CancellationToken ct = default)
            where T : notnull
        {
            if (!await store.TryMarkAsProcessedAsync(context.MessageId, ct))
            {
                Console.WriteLine($"    [idempotency] duplicate {context.MessageId} — skipped");
                return;
            }

            await next();
        }
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("--- Idempotent processing (dedup on message id) ---");

        var handler = new LedgerHandler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging(b => b.EnableIdempotency());
        services.AddSingleton<IMessageMiddleware, IdempotencyMiddleware>();
        services.AddSingleton<IMessageHandler<PaymentCaptured>>(handler);

        await using var sp = services.BuildServiceProvider();
        var bus = sp.GetRequiredService<IMessageBus>();

        var payment = new PaymentCaptured(Guid.NewGuid(), 99.00m);

        // Same MessageId redelivered three times — only the first must apply.
        // Reusing one MessageContext keeps MessageId stable across deliveries.
        var ctx = MessageContext.New(correlationId: "order-77");
        await bus.PublishAsync(payment, ctx);
        await bus.PublishAsync(payment, ctx);
        await bus.PublishAsync(payment, ctx);

        Console.WriteLine($"  deliveries attempted     : 3");
        Console.WriteLine($"  handler applications     : {handler.Applied} (expected 1)");
        Console.WriteLine($"  ledger balance           : {handler.Balance:C} (expected {99.00m:C})");
        Console.WriteLine();
    }
}
