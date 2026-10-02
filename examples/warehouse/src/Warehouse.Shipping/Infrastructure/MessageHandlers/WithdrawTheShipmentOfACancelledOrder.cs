using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Warehouse.Orders.Contracts.Events;

namespace Warehouse.Shipping.Infrastructure.MessageHandlers;

/// <summary>
///     An order was cancelled: its shipment, if one was made and has not left, is withdrawn.
/// </summary>
/// <remarks>
///     Idempotent by the shipment's state (<c>WithdrawShipmentsAction</c>). An order cancelled before it was
///     picked has no shipment, and this withdraws nothing.
/// </remarks>
[MessageHandler]
internal sealed partial class WithdrawTheShipmentOfACancelledOrder(IShippingInternalActions shipping)
    : IMessageHandler<OrderCancelled>
{
    public async Task HandleAsync(OrderCancelled message, MessageContext context, CancellationToken ct = default)
    {
        var withdrawn = await shipping.WithdrawShipments(orderId: message.OrderId, ct: ct).ConfigureAwait(false);

        if (withdrawn.IsFailure)
            throw new InvalidOperationException(
                $"The shipment of cancelled order {message.OrderId} could not be withdrawn: {withdrawn.Error}");
    }
}
