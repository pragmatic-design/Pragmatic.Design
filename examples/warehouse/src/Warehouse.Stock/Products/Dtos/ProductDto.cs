namespace Warehouse.Stock.Dtos;

/// <summary>
///     A product as a reader sees it: the name in their language, and every translation for whoever edits
///     it.
/// </summary>
[MapFrom<Product>]
[GenerateProjection]
public partial class ProductDto
{
    public Guid Id { get; init; }

    public string Sku { get; init; } = "";

    /// <summary>In the culture of the request.</summary>
    public string Name { get; init; } = "";

    /// <summary>Every translation of the name.</summary>
    [MapProperty("Name")]
    public LocalizedString? Names { get; init; }

    public int ReorderThreshold { get; init; }
}
