namespace Warehouse.Stock.Infrastructure.Authorization;

/// <summary>
///     Works the floor: reads the catalogue and the levels, receives goods — one by one or a supplier's
///     whole file — and picks orders. Corrects nothing.
/// </summary>
[Role("stock-clerk", "Reads the catalogue and the levels, receives goods and picks orders")]
[Grants(
    StockPermissions.Product.Read,
    StockPermissions.Location.Read,
    StockPermissions.StockLevel.Read,
    StockPermissions.StockLevel.Receive,
    StockPermissions.ImportedPart.Read,
    StockPermissions.PickList.Create)]
public sealed partial class StockClerkRole;
