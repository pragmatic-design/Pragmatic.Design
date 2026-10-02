namespace Warehouse.Stock.Products.Mutations;

/// <summary>
///     A product renamed, or given another reorder threshold. Only what is sent changes; the SKU does not,
///     because every order line already names it.
/// </summary>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(StockPermissions.Product.Update)]
[Endpoint(HttpVerb.Put, "api/products/{id}")]
[ReturnsDto<ProductDto>]
public partial class UpdateProductMutation : Mutation<Product>
{
    public required Guid Id { get; init; }

    public LocalizedString? Name { get; init; }

    [GreaterThanOrEqual(0)]
    public int? ReorderThreshold { get; init; }
}
