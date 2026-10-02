using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Warehouse.Orders.Events;

namespace Warehouse.Orders.Infrastructure.MessageHandlers;

/// <summary>The fulfilment process asked: the order is marked picked.</summary>
/// <remarks>Through the internal interface: a message arrives with no principal.</remarks>
[MessageHandler]
internal sealed partial class RecordThePickWhenTheProcessAsks(IOrdersInternalActions orders)
    : IMessageHandler<RecordOrderPicked>
{
    public async Task HandleAsync(RecordOrderPicked message, MessageContext context, CancellationToken ct = default)
    {
        var marked = await orders.MarkOrderPicked(id: message.OrderId, ct: ct).ConfigureAwait(false);

        if (marked.IsFailure)
            throw new InvalidOperationException($"Order {message.OrderId} could not be marked picked: {marked.Error}");
    }
}
