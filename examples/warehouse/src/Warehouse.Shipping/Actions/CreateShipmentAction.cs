using Warehouse.Stock.Contracts.Events;

namespace Warehouse.Shipping.Actions;

/// <summary>
///     A picked order becomes a shipment, once: the answer is whether this call made it.
/// </summary>
/// <remarks>
///     <para>
///         No <c>[Endpoint]</c>: shipments are made from what Stock publishes, by
///         <c>MakeAShipmentOfAPickedOrder</c>, not by calling this service's API.
///     </para>
///     <para>
///         Keyed on the event: a shipment already made from <see cref="EventId" /> answers <c>false</c> and
///         writes nothing. The unique index on <c>PickedEventId</c> is what holds when two deliveries of the
///         same event race past this lookup — the second save is refused, the handler throws, and the
///         redelivery finds the shipment.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(ShippingPermissions.Shipment.Create)]
public partial class CreateShipmentAction : DomainAction<bool>
{
    private IRepository<Shipment> _shipments = null!;

    public required Guid EventId { get; init; }

    public required Guid OrderId { get; init; }

    public required List<PickedLine> Lines { get; init; }

    public override async Task<Result<bool, IError>> Execute(CancellationToken ct = default)
    {
        if (await _shipments.ExistsAsync(Spec<Shipment>.Where(s => s.PickedEventId == EventId), ct).ConfigureAwait(false))
            return false;

        var shipment = Shipment.For(OrderId, EventId);
        foreach (var line in Lines)
            shipment.Lines.Add(ShipmentLine.Of(line.Sku, line.Quantity));

        _shipments.Add(shipment);
        return true;
    }
}
