using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Warehouse.Stock.Contracts;

namespace Warehouse.Stock.Infrastructure.RequestHandlers;

/// <summary>
///     Orders asks for an order's stock and waits: this answers, from whichever Stock instance took the
///     request off the queue.
/// </summary>
/// <remarks>
///     <para>
///         The one request/reply in the examples, and on purpose: placing an order cannot finish until
///         the stock is known to be held, so the answer belongs inside the customer's request. Everywhere
///         else a service tells another what happened and does not wait (Casework).
///     </para>
///     <para>
///         The queue is named after the request type — <c>requests.reserve-stock</c> — and not after
///         this module, so both instances consume the same queue and each request is answered once.
///     </para>
///     <para>
///         Through the internal interface: a request arrives with no principal, and the internal surface
///         is the one that runs as an internal call. The action's invoker owns the transaction.
///     </para>
/// </remarks>
[RequestHandler]
internal sealed partial class AnswerReservationRequests(IStockReservationsInternalActions reservations)
    : IRequestHandler<ReserveStock, StockReservation>
{
    public async Task<StockReservation> HandleAsync(
        ReserveStock request, MessageContext context, CancellationToken ct = default)
    {
        var held = await reservations
            .ReserveStock(orderId: request.OrderId, lines: [.. request.Lines], ct: ct)
            .ConfigureAwait(false);

        // A refusal that is not "short" — nothing here expects one — goes back to Orders as the
        // responder's error, which the requester reads as a service that could not answer.
        if (held.IsFailure)
            throw new InvalidOperationException(
                $"The stock for order {request.OrderId} could not be reserved: {held.Error}");

        return held.Value;
    }
}
