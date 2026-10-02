// The permissions that are not an entity's CRUD. Each is a constant of ShippingPermissions beside the CRUD ones.

// Handing a shipment to its carrier: from here it has left the building.
[assembly: Permission("shipping.shipment.dispatch", "Dispatch a shipment with its carrier", Category = "Shipping")]
