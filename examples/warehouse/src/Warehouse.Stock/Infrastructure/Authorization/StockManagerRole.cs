namespace Warehouse.Stock.Infrastructure.Authorization;

/// <summary>
///     Runs the warehouse: manages the catalogue and the locations, and is the one who may correct a level.
/// </summary>
[Role("stock-manager", "Manages the catalogue and the locations, and adjusts stock levels")]
[IncludesRole<StockClerkRole>]
[Grants(
    StockPermissions.Product.All,
    StockPermissions.Location.All,
    StockPermissions.StockLevel.Adjust)]
public sealed partial class StockManagerRole;
