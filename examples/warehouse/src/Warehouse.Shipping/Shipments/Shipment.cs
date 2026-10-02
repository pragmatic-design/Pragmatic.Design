namespace Warehouse.Shipping.Entities;

/// <summary>
///     What leaves for one order: its lines, the carrier that takes them, and the number the customer
///     follows them by.
/// </summary>
/// <remarks>
///     <para>
///         The order is a plain <c>OrderId</c> and the lines are what the picked event said: Shipping reads
///         neither Orders' database nor Stock's.
///     </para>
///     <para>
///         <see cref="PickedEventId" /> is the <c>EventId</c> of the <c>OrderPicked</c> this shipment was
///         made from, unique: it is what makes the consumer idempotent — the same event delivered twice
///         finds its shipment — and the unique index is what makes it so under a race as well, where a
///         lookup alone would let two deliveries both find nothing.
///     </para>
/// </remarks>
[Entity]
[StateMachine<ShipmentStatus>]
[Unique(nameof(PickedEventId))]
[Relation.OneToMany<ShipmentLine>.WithNavigation("Lines", Inverse = "Shipment", OnDelete = DeleteBehavior.Cascade)]
public partial class Shipment : DomainEventSource, IEntity
{
    /// <summary>What the customer follows the parcel by: <c>TRK-00000001</c>, from a database sequence.</summary>
    [LogicKey]
    [GeneratedValue("TRK-{SEQ:8}")]
    public string TrackingNumber { get; private set; } = "";

    /// <summary>The order it carries, by Orders' id.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The event this shipment was made from.</summary>
    public Guid PickedEventId { get; private set; }

    /// <summary>Who takes it. Known when it is dispatched, not before.</summary>
    [MaxLength(60)]
    public string? Carrier { get; private set; }

    /// <summary>How many parcels it went out as. Known when it is dispatched.</summary>
    public int Packages { get; private set; }

    public ShipmentStatus Status { get; private set; } = ShipmentStatus.Created;

    internal static Shipment For(Guid orderId, Guid pickedEventId)
    {
        var shipment = Create();
        shipment.OrderId = orderId;
        shipment.PickedEventId = pickedEventId;
        return shipment;
    }
}
