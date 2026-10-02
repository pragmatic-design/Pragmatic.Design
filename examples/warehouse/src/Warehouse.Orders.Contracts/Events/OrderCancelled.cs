using Pragmatic.Events;
using Pragmatic.Messaging.Attributes;

namespace Warehouse.Orders.Contracts.Events;

/// <summary>
///     An order was cancelled before it shipped. Published by Orders; Stock gives back what it held or
///     picked for it, and Shipping withdraws its shipment.
/// </summary>
/// <remarks>
///     <para>
///         One fact, and each service undoes its own part from its own state: Stock knows whether the
///         stock is still held or already off the shelf, Shipping whether a shipment was made. A command
///         per service chosen by Orders would need Orders to know what the others did, which is exactly
///         what their own databases already say.
///     </para>
///     <para>
///         <c>[CorrelationKey]</c> on the order: the fulfilment process of the order, if it started, is
///         what waits for the others to say they are done.
///     </para>
/// </remarks>
/// <param name="OrderId">The order, by Orders' id.</param>
/// <param name="OccurredAt">When it was cancelled. Orders' clock.</param>
public sealed record OrderCancelled([property: CorrelationKey] Guid OrderId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt), IIntegrationEvent;
