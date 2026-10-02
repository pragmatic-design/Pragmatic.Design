namespace Warehouse.Stock.Dtos;

/// <summary>A product below its reorder threshold: how many are on hand, and the threshold it is under.</summary>
public sealed class ProductToReorderDto
{
    public Guid ProductId { get; init; }

    public string Sku { get; init; } = "";

    /// <summary>On hand across every location.</summary>
    public int OnHand { get; init; }

    /// <summary>The product's own threshold, or the default when it has none.</summary>
    public int Threshold { get; init; }
}
