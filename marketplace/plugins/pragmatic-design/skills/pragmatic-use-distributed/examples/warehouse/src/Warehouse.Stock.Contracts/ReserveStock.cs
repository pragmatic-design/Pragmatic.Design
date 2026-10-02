namespace Warehouse.Stock.Contracts;

/// <summary>
///     Orders asks Stock to hold what an order needs: every line, or none of them.
/// </summary>
/// <remarks>
///     A request and not an event: the order cannot be placed until the answer is known, and the customer
///     is waiting for it. Sent over the broker with <c>IMessageBus.RequestAsync</c> and answered by
///     whichever Stock instance takes it off the queue.
/// </remarks>
public sealed record ReserveStock(Guid OrderId, IReadOnlyList<ReservationLine> Lines);
