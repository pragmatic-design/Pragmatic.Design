namespace Warehouse.Orders.Events;

/// <summary>
///     The fulfilment process asks for the order to be marked picked. Published by
///     <c>OrderFulfilmentSaga</c> after it saves its state; carried out by
///     <c>RecordThePickWhenTheProcessAsks</c>.
/// </summary>
/// <remarks>
///     A message and not a call, because a saga has no dependencies: it is persisted state plus decisions,
///     and its effects leave as messages.
/// </remarks>
public sealed record RecordOrderPicked(Guid OrderId);
