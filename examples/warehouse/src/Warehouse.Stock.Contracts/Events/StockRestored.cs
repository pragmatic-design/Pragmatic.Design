using Pragmatic.Events;
using Pragmatic.Messaging.Attributes;

namespace Warehouse.Stock.Contracts.Events;

/// <summary>
///     Stock undid its part of a cancelled order: what was held is available again, and what was picked is
///     back on its shelf. Published once per order; Orders' fulfilment process counts it as one of the
///     acknowledgements it waits for.
/// </summary>
/// <param name="OrderId">The cancelled order, by Orders' id.</param>
/// <param name="OccurredAt">When the stock was restored. Stock's clock.</param>
public sealed record StockRestored([property: CorrelationKey] Guid OrderId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt), IIntegrationEvent;
