using Pragmatic.Events;
using Pragmatic.Messaging.Attributes;

namespace Warehouse.Stock.Contracts.Events;

/// <summary>
///     An order's lines are off the shelves and ready to leave. Published by Stock when the picker is done;
///     Shipping makes a shipment of it.
/// </summary>
/// <remarks>
///     The lines travel on the event, by SKU and quantity: Shipping packs what it is told was picked, and
///     reads neither Stock's database nor Orders'.
/// </remarks>
/// <param name="OrderId">The order, by Orders' id.</param>
/// <param name="Lines">What was picked.</param>
/// <param name="OccurredAt">When the picking finished. Stock's clock.</param>
/// <remarks>
///     <c>[CorrelationKey]</c> on the order: it is the conversation Orders' fulfilment process follows.
/// </remarks>
public sealed record OrderPicked(
    [property: CorrelationKey] Guid OrderId, IReadOnlyList<PickedLine> Lines, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt), IIntegrationEvent;
