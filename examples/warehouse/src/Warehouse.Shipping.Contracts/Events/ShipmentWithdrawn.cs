using Pragmatic.Events;
using Pragmatic.Messaging.Attributes;

namespace Warehouse.Shipping.Contracts.Events;

/// <summary>
///     A shipment that had not left was withdrawn, because its order was cancelled. Published by Shipping;
///     Orders' fulfilment process counts it as one of the acknowledgements it waits for.
/// </summary>
/// <param name="ShipmentId">Shipping's own row.</param>
/// <param name="OrderId">The cancelled order, by Orders' id.</param>
/// <param name="OccurredAt">When it was withdrawn. Shipping's clock.</param>
public sealed record ShipmentWithdrawn(Guid ShipmentId, [property: CorrelationKey] Guid OrderId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt), IIntegrationEvent;
