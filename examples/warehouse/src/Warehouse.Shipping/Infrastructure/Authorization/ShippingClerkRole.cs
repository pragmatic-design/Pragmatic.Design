namespace Warehouse.Shipping.Infrastructure.Authorization;

/// <summary>
///     Works the dock: reads the shipments and hands them to their carriers.
/// </summary>
[Role("shipping-clerk", "Reads shipments and dispatches them")]
[Grants(
    ShippingPermissions.Shipment.Read,
    ShippingPermissions.Shipment.Dispatch)]
public sealed partial class ShippingClerkRole;
