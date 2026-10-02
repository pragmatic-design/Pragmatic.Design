using Microsoft.Extensions.Logging;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Warehouse.Stock.Contracts.Events;

namespace Warehouse.Shipping.Infrastructure.MessageHandlers;

/// <summary>
///     Stock picked an order: Shipping makes a shipment of it.
/// </summary>
/// <remarks>
///     Idempotent on the event's id (<c>CreateShipmentAction</c>): the same <c>OrderPicked</c> delivered
///     twice makes one shipment, and the second delivery is logged and acknowledged. Through the internal
///     interface, because a message arrives with no principal.
/// </remarks>
[MessageHandler]
internal sealed partial class MakeAShipmentOfAPickedOrder(
    IShippingInternalActions shipping,
    ILogger<MakeAShipmentOfAPickedOrder> logger) : IMessageHandler<OrderPicked>
{
    public async Task HandleAsync(OrderPicked message, MessageContext context, CancellationToken ct = default)
    {
        var created = await shipping
            .CreateShipment(eventId: message.EventId, orderId: message.OrderId, lines: [.. message.Lines], ct: ct)
            .ConfigureAwait(false);

        if (created.IsFailure)
            throw new InvalidOperationException(
                $"The shipment of order {message.OrderId} could not be made: {created.Error}");

        if (!created.Value)
            LogAlreadyMade(message.OrderId, message.EventId);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Order {OrderId} already has its shipment from event {EventId}: nothing was written.")]
    private partial void LogAlreadyMade(Guid orderId, Guid eventId);
}
