using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.Extensions;

namespace Pragmatic.Messaging.Samples.Samples;

/// <summary>
///     Smallest end-to-end messaging flow: register a handler, register the
///     in-memory bus via AddPragmaticMessaging(), publish a message, verify
///     the handler observed it. No transport, no outbox, no saga — just the
///     core publish/handle contract.
/// </summary>
public static class InMemoryRoundTripSample
{
    public record OrderPlaced(Guid OrderId, decimal Total);

    public sealed class OrderPlacedHandler : IMessageHandler<OrderPlaced>
    {
        public List<OrderPlaced> Received { get; } = [];

        public Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct)
        {
            Received.Add(message);
            return Task.CompletedTask;
        }
    }

    public static async Task Run()
    {
        Console.WriteLine("--- In-memory round trip (publish -> handle) ---");

        var handler = new OrderPlacedHandler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging();
        services.AddSingleton<IMessageHandler<OrderPlaced>>(handler);

        await using var sp = services.BuildServiceProvider();
        var bus = sp.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 149.95m));
        await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 32.10m));
        await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(),  8.00m));

        Console.WriteLine($"  messages published     : 3");
        Console.WriteLine($"  handler received       : {handler.Received.Count}");
        Console.WriteLine($"  total amount           : {handler.Received.Sum(m => m.Total):C}");
        Console.WriteLine();
    }
}
