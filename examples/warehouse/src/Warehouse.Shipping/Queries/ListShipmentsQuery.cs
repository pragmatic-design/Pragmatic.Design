namespace Warehouse.Shipping.Queries;

/// <summary>The shipments, or the ones of one order.</summary>
[Query<Shipment, ShipmentDto>]
[RequirePermission(ShippingPermissions.Shipment.Read)]
[Endpoint(HttpVerb.Get, "api/shipments")]
public partial class ListShipmentsQuery
{
    [Filter]
    public Guid? OrderId { get; init; }
}
