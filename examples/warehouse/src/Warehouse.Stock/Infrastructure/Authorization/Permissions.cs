// The permissions that are not an entity's CRUD. Each is a constant of StockPermissions beside the CRUD ones.

// Putting goods on the shelf: the receipt that raises a level.
[assembly: Permission("stock.stock-level.receive", "Receive goods at a location", Category = "Stock")]

// Correcting a level after a count. A permission of its own, outside receiving: it changes what the system
// believes is on the shelf, and nobody should hold it as a side effect of being allowed to unload a truck.
[assembly: Permission("stock.stock-level.adjust", "Adjust a stock level after a count, with a reason", Category = "Stock")]
