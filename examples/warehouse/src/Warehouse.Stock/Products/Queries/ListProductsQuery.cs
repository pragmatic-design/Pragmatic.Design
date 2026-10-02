namespace Warehouse.Stock.Products.Queries;

/// <summary>
///     The catalogue, by SKU.
/// </summary>
/// <remarks>
///     Filtered by SKU and not by name: the name is a value per language, and a search across
///     translations is a decision about which language to search in that this example has not made yet.
/// </remarks>
[Query<Product, ProductDto>(Paged = true)]
[RequirePermission(StockPermissions.Product.Read)]
[Endpoint(HttpVerb.Get, "api/products")]
public partial class ListProductsQuery
{
    [Filter]
    public string? Sku { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? SkuSort { get; init; }
}
