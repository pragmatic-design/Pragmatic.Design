using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Warehouse.Stock.Contracts.Events;

namespace Warehouse.Orders.Infrastructure.MessageHandlers;

/// <summary>
///     Stock gave back a hold nobody confirmed: the order it was for is cancelled.
/// </summary>
/// <remarks>
///     <para>
///         Through the internal interface: a message arrives with no principal, and the internal surface
///         is the one that runs as an internal call.
///     </para>
///     <para>
///         Idempotent by the order's state, not by the event's id — see <c>ExpireOrderMutation</c>. An
///         order can receive one expiry per hold, and every one after the first changes nothing.
///     </para>
/// </remarks>
[MessageHandler]
internal sealed partial class CancelTheOrderWhoseHoldExpired(IOrdersInternalActions orders)
    : IMessageHandler<ReservationExpired>
{
    public async Task HandleAsync(ReservationExpired message, MessageContext context, CancellationToken ct = default)
    {
        var expired = await orders.ExpireOrder(id: message.OrderId, ct: ct).ConfigureAwait(false);

        // An expiry for an order this service does not hold is not something to acknowledge and forget:
        // the nack sends it to the dead letter, where somebody looks.
        if (expired.IsFailure)
            throw new InvalidOperationException(
                $"Order {message.OrderId} could not be cancelled after its hold expired: {expired.Error}");
    }
}
