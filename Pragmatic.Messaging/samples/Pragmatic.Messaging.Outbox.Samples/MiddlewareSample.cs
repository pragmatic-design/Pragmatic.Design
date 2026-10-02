using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Extensions;

namespace Pragmatic.Messaging.Outbox.Samples;

/// <summary>
///     Custom <see cref="IMessageMiddleware" />: middleware wraps handler
///     execution and can observe, enrich, or short-circuit a dispatch. The bus
///     composes registered middleware by ascending <see cref="IMessageMiddleware.Order" />
///     (lower = outermost) around the handler invocation. Middleware is registered
///     directly in DI as <c>IMessageMiddleware</c>. This sample registers two and
///     shows the wrap order plus a short-circuit decision.
///
///     Note the middleware contract is generic — <c>InvokeAsync&lt;T&gt;(T message, ...)</c>
///     and the continuation is a parameterless <see cref="MessageHandlerDelegate" />.
/// </summary>
public static class MiddlewareSample
{
    public sealed record TransferRequested(string From, string To, decimal Amount);

    public sealed class TransferHandler : IMessageHandler<TransferRequested>
    {
        public int Handled { get; private set; }

        public Task HandleAsync(TransferRequested message, MessageContext context, CancellationToken ct)
        {
            Handled++;
            Console.WriteLine($"    [handler] transferred {message.Amount:C} {message.From} -> {message.To}");
            return Task.CompletedTask;
        }
    }

    /// <summary>Outermost: logs entry/exit around the whole pipeline.</summary>
    public sealed class LoggingMiddleware : IMessageMiddleware
    {
        public int Order => -100;

        public async Task InvokeAsync<T>(T message, MessageContext context, MessageHandlerDelegate next, CancellationToken ct = default)
            where T : notnull
        {
            Console.WriteLine($"    [logging] -> {typeof(T).Name}");
            await next();
            Console.WriteLine($"    [logging] <- {typeof(T).Name}");
        }
    }

    /// <summary>Inner: rejects transfers above a threshold by skipping <c>next</c>.</summary>
    public sealed class AmountGuardMiddleware : IMessageMiddleware
    {
        public int Order => 0;

        public Task InvokeAsync<T>(T message, MessageContext context, MessageHandlerDelegate next, CancellationToken ct = default)
            where T : notnull
        {
            if (message is TransferRequested { Amount: > 10_000m } big)
            {
                Console.WriteLine($"    [guard] BLOCKED {big.Amount:C} (over limit) — handler skipped");
                return Task.CompletedTask; // short-circuit: do not call next()
            }

            return next();
        }
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("--- Custom middleware pipeline (order + short-circuit) ---");

        var handler = new TransferHandler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging();
        // Middleware is registered directly as IMessageMiddleware; the bus orders
        // by IMessageMiddleware.Order and wraps the handler in that sequence.
        services.AddSingleton<IMessageMiddleware, LoggingMiddleware>();
        services.AddSingleton<IMessageMiddleware, AmountGuardMiddleware>();
        services.AddSingleton<IMessageHandler<TransferRequested>>(handler);

        await using var sp = services.BuildServiceProvider();
        var bus = sp.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new TransferRequested("alice", "bob", 250m));
        await bus.PublishAsync(new TransferRequested("alice", "carol", 50_000m));

        Console.WriteLine($"  handler invocations      : {handler.Handled} (1 allowed, 1 blocked by guard)");
        Console.WriteLine();
    }
}
