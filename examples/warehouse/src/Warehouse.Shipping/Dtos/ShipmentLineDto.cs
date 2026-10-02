namespace Warehouse.Shipping.Dtos;

/// <summary>A line as it is read back.</summary>
[MapFrom<ShipmentLine>]
public partial class ShipmentLineDto
{
    public Guid Id { get; init; }

    public string Sku { get; init; } = "";

    public int Quantity { get; init; }
}
