using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;

namespace Pragmatic.Aot.Messaging;

/// <summary>Records the order it receives.</summary>
[MessageHandler]
public sealed partial class OrderPlacedHandler(Received received) : IMessageHandler<OrderPlaced>
{
    /// <inheritdoc />
    public Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
    {
        received.Add(message.OrderId);
        return Task.CompletedTask;
    }
}
