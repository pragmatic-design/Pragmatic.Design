namespace Warehouse.Stock.Products.Mutations;

/// <summary>
///     A product enters the catalogue: its SKU, its name in each language, and when to reorder it.
/// </summary>
/// <remarks>
///     A SKU already in the catalogue is refused by the unique index the SKU's <c>[LogicKey]</c> declares,
///     and answered 409 naming the field — not by a lookup here, which two concurrent requests would both
///     pass.
/// </remarks>
[Mutation(Mode = MutationMode.Create)]
[RequirePermission(StockPermissions.Product.Create)]
[Endpoint(HttpVerb.Post, "api/products")]
[CreatedAt("/api/products/{Id}")]
[ReturnsDto<ProductDto>]
public partial class CreateProductMutation : Mutation<Product>
{
    [Required]
    [MaxLength(40)]
    public required string Sku { get; init; }

    /// <summary>The name in as many cultures as it is sold in: <c>{ "en-US": "Pallet jack", "it-IT": "Transpallet" }</c>.</summary>
    public required LocalizedString Name { get; init; }

    [GreaterThanOrEqual(0)]
    public int ReorderThreshold { get; init; }
}
