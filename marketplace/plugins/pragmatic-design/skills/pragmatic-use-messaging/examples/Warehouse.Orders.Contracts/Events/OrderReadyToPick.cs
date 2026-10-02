using Pragmatic.Events;
using Pragmatic.Messaging.Attributes;

namespace Warehouse.Orders.Contracts.Events;

/// <summary>
///     The customer confirmed a reserved order: it is ready to be picked. Published by Orders; Stock makes
///     the order's holds permanent, so their expiry gives nothing back.
/// </summary>
/// <param name="OrderId">The order, by Orders' id.</param>
/// <param name="OccurredAt">When it was confirmed. Orders' clock.</param>
/// <remarks>
///     <c>[CorrelationKey]</c> on the order: this is the event that starts Orders' fulfilment process for it.
/// </remarks>
public sealed record OrderReadyToPick([property: CorrelationKey] Guid OrderId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt), IIntegrationEvent;
