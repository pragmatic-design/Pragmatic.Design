namespace Warehouse.Stock.Dtos;

/// <summary>The products to reorder, and the default threshold the answer was computed with.</summary>
public sealed class ProductsToReorderDto
{
    /// <summary>The default threshold in force when the list was read — what a product without its own uses.</summary>
    public int Threshold { get; init; }

    public List<ProductToReorderDto> Products { get; init; } = [];
}
