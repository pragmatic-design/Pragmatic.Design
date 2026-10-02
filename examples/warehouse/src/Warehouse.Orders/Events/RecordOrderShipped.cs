namespace Warehouse.Orders.Events;

/// <summary>
///     The fulfilment process asks for the order to be marked shipped. Published by
///     <c>OrderFulfilmentSaga</c>; carried out by <c>RecordTheShipmentWhenTheProcessAsks</c>.
/// </summary>
public sealed record RecordOrderShipped(Guid OrderId);
