using Microsoft.Extensions.Options;
using Warehouse.Stock.Infrastructure.Configuration;

namespace Warehouse.Stock.Products.Actions;

/// <summary>
///     The products to reorder: fewer on hand, across every location, than their threshold — their own,
///     or <see cref="ReorderOptions.DefaultReorderThreshold" /> when they have none.
/// </summary>
/// <remarks>
///     <para>
///         An action and not a query: which products it lists depends on a setting, not only on what is
///         stored, and a query declares its filter from the stored columns.
///     </para>
///     <para>
///         <c>IOptionsMonitor</c>, not <c>IOptions</c>: the default threshold changes at runtime through the
///         Agent, and <c>IOptions</c> is read once, when the host starts.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(StockPermissions.Product.Read)]
[Endpoint(HttpVerb.Get, "api/products/to-reorder")]
public partial class ListProductsToReorderAction : DomainAction<ProductsToReorderDto>
{
    private IOptionsMonitor<ReorderOptions> _options = null!;
    private IReadRepository<Product> _products = null!;
    private IReadRepository<StockLevel> _levels = null!;

    public override async Task<Result<ProductsToReorderDto, IError>> Execute(CancellationToken ct = default)
    {
        var threshold = _options.CurrentValue.DefaultReorderThreshold;
        var products = await _products.FindAsync(Spec<Product>.Where(p => true), ct).ConfigureAwait(false);
        var levels = await _levels.FindAsync(Spec<StockLevel>.Where(l => true), ct).ConfigureAwait(false);
        var onHand = levels.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Sum(l => l.OnHand));

        return new ProductsToReorderDto
        {
            Threshold = threshold,
            Products =
            [
                .. products
                    .Select(product => new ProductToReorderDto
                    {
                        ProductId = product.PersistenceId,
                        Sku = product.Sku,
                        OnHand = onHand.GetValueOrDefault(product.PersistenceId),
                        Threshold = product.ReorderThreshold > 0 ? product.ReorderThreshold : threshold,
                    })
                    .Where(line => line.OnHand < line.Threshold),
            ],
        };
    }
}
