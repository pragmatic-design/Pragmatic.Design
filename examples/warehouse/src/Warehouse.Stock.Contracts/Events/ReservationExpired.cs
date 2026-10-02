using Pragmatic.Events;

namespace Warehouse.Stock.Contracts.Events;

/// <summary>
///     A hold nobody confirmed in time was given back: its stock is available again. Published by Stock;
///     Orders cancels the order it was held for.
/// </summary>
/// <remarks>
///     One per hold, and an order can have several — a line held across two locations is two holds. The
///     consumer acts on the order once and treats the rest as already done.
/// </remarks>
/// <param name="ReservationId">Stock's own row.</param>
/// <param name="OrderId">The order in Orders' database the hold was for.</param>
/// <param name="OccurredAt">When it was given back. Stock's clock.</param>
public sealed record ReservationExpired(Guid ReservationId, Guid OrderId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt), IIntegrationEvent;
