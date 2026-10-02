namespace Warehouse.Stock.Entities;

/// <summary>The rules a stock level is read by, beside the generated <c>ById</c>.</summary>
public static partial class StockLevelSpecifications
{
    /// <summary>The one level of a product at a location — the pair the <c>[LogicKey]</c> makes unique.</summary>
    public static Specification<StockLevel> Of(Guid productId, Guid locationId)
        => Spec<StockLevel>.Where(l => l.ProductId == productId && l.LocationId == locationId);
}
