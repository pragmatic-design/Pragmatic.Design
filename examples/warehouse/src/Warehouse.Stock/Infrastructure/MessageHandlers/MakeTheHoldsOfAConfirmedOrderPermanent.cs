using Microsoft.Extensions.Logging;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Warehouse.Orders.Contracts.Events;

namespace Warehouse.Stock.Infrastructure.MessageHandlers;

/// <summary>
///     A confirmed order keeps its stock: its holds stop expiring.
/// </summary>
/// <remarks>
///     <para>
///         The queue is named after this module — <c>stock.order-ready-to-pick</c> — so both Stock
///         instances consume it and each confirmation is handled once.
///     </para>
///     <para>
///         Idempotent by state rather than by remembering the event: only holds still <c>Held</c> are
///         confirmed, so a redelivery confirms nothing and writes nothing.
///     </para>
/// </remarks>
[MessageHandler]
internal sealed partial class MakeTheHoldsOfAConfirmedOrderPermanent(
    IStockReservationsInternalActions reservations,
    ILogger<MakeTheHoldsOfAConfirmedOrderPermanent> logger) : IMessageHandler<OrderReadyToPick>
{
    public async Task HandleAsync(OrderReadyToPick message, MessageContext context, CancellationToken ct = default)
    {
        var confirmed = await reservations
            .ConfirmReservations(orderId: message.OrderId, ct: ct)
            .ConfigureAwait(false);

        if (confirmed.IsFailure)
            throw new InvalidOperationException(
                $"The holds of order {message.OrderId} could not be confirmed: {confirmed.Error}");

        LogConfirmed(message.OrderId, confirmed.Value);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Order {OrderId} confirmed: {Confirmed} hold(s) made permanent.")]
    private partial void LogConfirmed(Guid orderId, int confirmed);
}
