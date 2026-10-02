using Warehouse.Stock.Contracts.Events;

namespace Warehouse.Stock.Entities;

/// <summary>
///     Stock undid its part of a cancelled order: once, and it says so.
/// </summary>
/// <remarks>
///     <para>
///         One per order, by the unique index on <see cref="OrderId" />: it is what makes the compensation
///         idempotent — a second delivery of the cancellation finds it and changes nothing — and the index
///         holds when two deliveries race past the lookup.
///     </para>
///     <para>
///         Its creation raises <c>StockRestored</c>, through the outbox, in the transaction that gives the
///         stock back: the acknowledgement Orders' fulfilment process waits for.
///     </para>
/// </remarks>
[Entity]
[Unique(nameof(OrderId))]
public partial class StockRestoration : DomainEventSource, IEntity
{
    /// <summary>The cancelled order, by Orders' id.</summary>
    public Guid OrderId { get; private set; }

    public DateTimeOffset RestoredAt { get; private set; }

    internal static StockRestoration Of(Guid orderId, DateTimeOffset restoredAt)
    {
        var restoration = Create();
        restoration.OrderId = orderId;
        restoration.RestoredAt = restoredAt;
        restoration.RaiseEvent(new StockRestored(orderId, restoredAt));
        return restoration;
    }
}
