namespace Warehouse.Stock.Dtos;

/// <summary>
///     A product's stock at one location: what is there, what is promised, and what is left to promise.
/// </summary>
[MapFrom<StockLevel>]
[GenerateProjection]
public partial class StockLevelDto
{
    public Guid Id { get; init; }

    public Guid ProductId { get; init; }

    public Guid LocationId { get; init; }

    public int OnHand { get; init; }

    public int Reserved { get; init; }

    /// <summary>On hand minus reserved: what an order can still be promised.</summary>
    [MapIgnore]
    public int Available => OnHand - Reserved;
}
