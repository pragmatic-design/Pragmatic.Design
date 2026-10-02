using Pragmatic.Messaging;
using Pragmatic.Messaging.RequestReply;
using Warehouse.Orders.Errors;
using Warehouse.Stock.Contracts;

namespace Warehouse.Orders.Actions;

/// <summary>
///     The customer sends the draft, and waits while its stock is held: <c>Draft → Placed → Reserved</c>,
///     or nothing at all.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why the customer waits.</b> An order that is placed and then found short is a promise taken
///         back, so the answer has to be inside this request: Orders asks Stock over the broker with
///         <c>RequestAsync</c> and does not answer until Stock has. It is the one exchange in the examples
///         that works this way; every other one between services is an event nobody waits for.
///     </para>
///     <para>
///         <b>Why over the broker and not HTTP.</b> A <c>RemoteBoundary</c> — the framework's default for
///         one service calling another — would need Orders to know where Stock is. On the broker it does
///         not: the request goes to a queue, both Stock instances consume it, and whichever is up
///         answers. The gateway balances the customers; the queue balances Orders.
///     </para>
///     <para>
///         <c>ByBody</c>, because the two moves are the body's and only on a full answer: a short one is
///         409 with the lines, a silent Stock is 503 within <c>Messaging:RequestReplyTimeout</c>, and in
///         both cases nothing is saved — the order is still a draft. An order that is not a draft is
///         refused by the first move, before Stock is asked anything.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(OrdersPermissions.Order.Place)]
[LoadEntity<Order>(nameof(Id), Include = nameof(Order.Lines))]
[TransitionsTo<OrderStatus>(OrderStatus.Reserved, When = TransitionTiming.ByBody)]
[Endpoint(HttpVerb.Post, "api/orders/{id}/place")]
// 200 and not the 201 of a POST action: placing creates nothing a Location could point at.
[HttpStatus(200)]
public partial class PlaceOrderAction
    : DomainAction<OrderDto, NotFoundError, ConflictError, InsufficientStockError, StockUnansweredError>
{
    private IMessageBus _bus = null!;

    public required Guid Id { get; init; }

    public override async Task<Result<OrderDto, IError>> Execute(CancellationToken ct = default)
    {
        var placed = _order.TransitionTo(OrderStatus.Placed);
        if (placed.IsFailure)
            return Result<OrderDto, IError>.Failure(placed.Error);

        StockReservation answer;
        try
        {
            answer = await _bus
                .RequestAsync<ReserveStock, StockReservation>(
                    new ReserveStock(
                        _order.PersistenceId,
                        [.. _order.Lines.Select(line => new ReservationLine(line.Sku, line.Quantity))]),
                    ct)
                .ConfigureAwait(false);
        }
        catch (RequestReplyException)
        {
            // No reply within the timeout, or Stock's handler failed: either way nobody knows whether the
            // stock is there, and the order is not placed on a guess.
            return new StockUnansweredError();
        }

        if (!answer.IsComplete)
            return new InsufficientStockError
            {
                ShortLines = [.. answer.Lines
                    .Where(line => !line.Reserved)
                    .Select(line => new ShortLine(line.Sku, line.Requested, line.Available))]
            };

        // A complete answer may still backorder part of a line, when Stock accepts backorders. The answer's
        // lines are the request's, in the same order.
        foreach (var (line, answered) in _order.Lines.Zip(answer.Lines))
        {
            if (answered.Backordered > 0)
                line.MarkBackordered(answered.Backordered);
        }

        var reserved = _order.TransitionTo(OrderStatus.Reserved);
        if (reserved.IsFailure)
            return Result<OrderDto, IError>.Failure(reserved.Error);

        return OrderDto.FromEntity(_order);
    }
}
