using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.Extensions;

namespace Pragmatic.Messaging.Samples.Samples;

/// <summary>
///     MessageContext carries correlation/tenant/user metadata through the
///     handler pipeline. Publishing with an explicit context propagates those
///     values to the handler; omitting it creates a fresh context with a new
///     message id. This is the per-request trace information you'd stamp onto
///     logs, audit records, and downstream messages.
/// </summary>
public static class CorrelationContextSample
{
    public record ShipmentReady(Guid ShipmentId);

    public sealed class ShipmentReadyHandler : IMessageHandler<ShipmentReady>
    {
        public List<(ShipmentReady msg, MessageContext ctx)> Received { get; } = [];

        public Task HandleAsync(ShipmentReady message, MessageContext context, CancellationToken ct)
        {
            Received.Add((message, context));
            return Task.CompletedTask;
        }
    }

    public static async Task Run()
    {
        Console.WriteLine("--- MessageContext propagation ---");

        var handler = new ShipmentReadyHandler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging();
        services.AddSingleton<IMessageHandler<ShipmentReady>>(handler);

        await using var sp = services.BuildServiceProvider();
        var bus = sp.GetRequiredService<IMessageBus>();

        // Caller-supplied context — this is how you'd stamp an inbound HTTP
        // correlation id onto a downstream async message.
        var outbound = MessageContext.New(
            correlationId: "req-4f2c",
            tenantId: "acme",
            userId: "user-42");
        await bus.PublishAsync(new ShipmentReady(Guid.NewGuid()), outbound);

        // Default context — the bus generates a fresh message id and leaves
        // correlation/tenant/user null.
        await bus.PublishAsync(new ShipmentReady(Guid.NewGuid()));

        Console.WriteLine($"  messages received        : {handler.Received.Count}");
        foreach (var (msg, ctx) in handler.Received)
        {
            Console.WriteLine(
                $"    - shipment={msg.ShipmentId:N}" +
                $"  corr={ctx.CorrelationId ?? "<none>"}" +
                $"  tenant={ctx.TenantId ?? "<none>"}" +
                $"  user={ctx.UserId ?? "<none>"}");
        }
        Console.WriteLine();
    }
}
