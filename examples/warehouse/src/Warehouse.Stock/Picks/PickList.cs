using Warehouse.Stock.Contracts.Events;

namespace Warehouse.Stock.Entities;

/// <summary>
///     An order picked: which order, and when. Its creation is what tells the others the order is off the
///     shelves.
/// </summary>
/// <remarks>
///     <para>
///         One per order, by the unique index on <see cref="OrderId" />: an order is picked once, and a
///         second pick of it is refused rather than a second shipment made downstream.
///     </para>
///     <para>
///         It raises <c>OrderPicked</c> with the lines, through the outbox, in the transaction that turns
///         the holds into movements — the one event per order that Shipping and Orders' process wait for.
///         The reservations cannot raise it themselves: there is one per location, and the event is about
///         the order.
///     </para>
/// </remarks>
[Entity]
[Unique(nameof(OrderId))]
public partial class PickList : DomainEventSource, IEntity
{
    /// <summary>The order picked, by Orders' id.</summary>
    public Guid OrderId { get; private set; }

    public DateTimeOffset PickedAt { get; private set; }

    internal static PickList Of(Guid orderId, IReadOnlyList<PickedLine> lines, DateTimeOffset pickedAt)
    {
        var pick = Create();
        pick.OrderId = orderId;
        pick.PickedAt = pickedAt;
        pick.RaiseEvent(new OrderPicked(orderId, lines, pickedAt));
        return pick;
    }
}
