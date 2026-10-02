using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.Extensions;

namespace Pragmatic.Messaging.Samples.Samples;

/// <summary>
///     PublishAsync vs SendAsync: Publish fans out to every handler registered
///     for the message type; Send targets a single consumer (point-to-point
///     queue semantics). In an in-memory bus only one handler is registered
///     per type, so the two methods look identical — but the intent and the
///     downstream transport wiring differ, and the choice becomes meaningful
///     as soon as RabbitMQ / Kafka enter the picture.
/// </summary>
public static class SendVsPublishSample
{
    public record InvoiceIssued(Guid InvoiceId, decimal Amount);

    public sealed class InvoiceIssuedHandler : IMessageHandler<InvoiceIssued>
    {
        public int Count { get; private set; }
        public decimal Total { get; private set; }

        public Task HandleAsync(InvoiceIssued message, MessageContext context, CancellationToken ct)
        {
            Count++;
            Total += message.Amount;
            return Task.CompletedTask;
        }
    }

    public static async Task Run()
    {
        Console.WriteLine("--- PublishAsync vs SendAsync ---");

        var handler = new InvoiceIssuedHandler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging();
        services.AddSingleton<IMessageHandler<InvoiceIssued>>(handler);

        await using var sp = services.BuildServiceProvider();
        var bus = sp.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new InvoiceIssued(Guid.NewGuid(), 149.95m));
        await bus.PublishAsync(new InvoiceIssued(Guid.NewGuid(),  32.10m));
        await bus.SendAsync(new InvoiceIssued(Guid.NewGuid(), 520.00m));
        await bus.SendAsync(new InvoiceIssued(Guid.NewGuid(),  12.50m));

        Console.WriteLine($"  messages observed        : {handler.Count}");
        Console.WriteLine($"  total amount             : {handler.Total:C}");
        Console.WriteLine("  semantics note           : in-memory bus treats Publish/Send symmetrically.");
        Console.WriteLine("                            RabbitMQ/Kafka transports preserve the distinction.");
        Console.WriteLine();
    }
}
