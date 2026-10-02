using Pragmatic.Events;
using Pragmatic.Messaging.Attributes;

namespace Warehouse.Shipping.Contracts.Events;

/// <summary>
///     A shipment left with its carrier. Published by Shipping; the order it carries is shipped.
/// </summary>
/// <param name="ShipmentId">Shipping's own row.</param>
/// <param name="OrderId">The order it carries, by Orders' id.</param>
/// <param name="TrackingNumber">What the customer follows the parcel by.</param>
/// <param name="OccurredAt">When it was dispatched. Shipping's clock.</param>
/// <remarks>
///     <c>[CorrelationKey]</c> on the order: it is the conversation Orders' fulfilment process follows.
/// </remarks>
public sealed record ShipmentDispatched(
    Guid ShipmentId, [property: CorrelationKey] Guid OrderId, string TrackingNumber, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt), IIntegrationEvent;
