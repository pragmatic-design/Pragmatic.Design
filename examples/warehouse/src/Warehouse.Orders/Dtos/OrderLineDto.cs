namespace Warehouse.Orders.Dtos;

/// <summary>A line as it is read back.</summary>
[MapFrom<OrderLine>]
public partial class OrderLineDto
{
    public Guid Id { get; init; }

    public string Sku { get; init; } = "";

    public int Quantity { get; init; }

    /// <summary>How many of the quantity wait for goods. Zero when the line was held whole.</summary>
    public int Backordered { get; init; }
}
