// The permissions that are not an entity's CRUD. Each is a constant of OrdersPermissions beside the CRUD ones.

// Sending a draft: from here on the order asks the other services for things.
[assembly: Permission("orders.order.place", "Place a drafted order", Category = "Orders")]

// Confirming a reserved order: its stock stops expiring and it can be picked.
[assembly: Permission("orders.order.confirm", "Confirm a reserved order", Category = "Orders")]

// Stopping an order before it leaves.
[assembly: Permission("orders.order.cancel", "Cancel an order that has not shipped", Category = "Orders")]
