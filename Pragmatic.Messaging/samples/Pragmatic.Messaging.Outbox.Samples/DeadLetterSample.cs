using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Extensions;

namespace Pragmatic.Messaging.Outbox.Samples;

/// <summary>
///     Dead-letter store: messages that exhaust their retry budget are parked in
///     an <see cref="IDeadLetterStore" /> for inspection and optional replay.
///     The in-memory store (<see cref="InMemoryDeadLetterStore" />) is registered
///     directly in DI. This sample simulates a poison message: a handler that
///     always throws, a middleware that catches the failure and writes a
///     <see cref="DeadLetterMessage" /> (a positional record:
///     MessageType, Payload, Error, RetryCount, Context, FailedAt), then reads
///     the parked entries back via <see cref="IDeadLetterStore.GetAllAsync" />.
/// </summary>
public static class DeadLetterSample
{
    public sealed record EmailQueued(string To, string Subject);

    /// <summary>Always fails — stands in for a genuinely unprocessable message.</summary>
    public sealed class FailingEmailHandler : IMessageHandler<EmailQueued>
    {
        public Task HandleAsync(EmailQueued message, MessageContext context, CancellationToken ct)
            => throw new InvalidOperationException("SMTP relay refused the message");
    }

    /// <summary>
    ///     Catches a handler failure and writes a <see cref="DeadLetterMessage" />.
    ///     A real pipeline would dead-letter only after the retry policy is
    ///     exhausted; here a single failure parks the message for brevity.
    /// </summary>
    public sealed class DeadLetterMiddleware(IDeadLetterStore store) : IMessageMiddleware
    {
        public int Order => -50;

        public async Task InvokeAsync<T>(T message, MessageContext context, MessageHandlerDelegate next, CancellationToken ct = default)
            where T : notnull
        {
            try
            {
                await next();
            }
            catch (Exception ex)
            {
                await store.StoreAsync(new DeadLetterMessage(
                    MessageType: typeof(T).FullName!,
                    Payload: JsonSerializer.Serialize(message),
                    Error: ex.Message,
                    RetryCount: context.RetryCount,
                    Context: context,
                    FailedAt: DateTimeOffset.UtcNow), ct);
                Console.WriteLine($"    [dead-letter] parked {typeof(T).Name}: {ex.Message}");
            }
        }
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("--- Dead-letter store (poison message capture) ---");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging();
        services.AddSingleton<IDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddSingleton<IMessageMiddleware, DeadLetterMiddleware>();
        services.AddSingleton<IMessageHandler<EmailQueued>, FailingEmailHandler>();

        await using var sp = services.BuildServiceProvider();
        var bus = sp.GetRequiredService<IMessageBus>();
        var store = sp.GetRequiredService<IDeadLetterStore>();

        await bus.PublishAsync(new EmailQueued("guest@example.com", "Booking confirmed"));
        await bus.PublishAsync(new EmailQueued("guest2@example.com", "Receipt"));

        var parked = await store.GetAllAsync();
        Console.WriteLine($"  dead-lettered messages   : {parked.Count} (expected 2)");
        foreach (var dl in parked)
            Console.WriteLine($"    - {dl.MessageType.Split('.')[^1]} | retries={dl.RetryCount} | error='{dl.Error}'");
        Console.WriteLine();
    }
}
