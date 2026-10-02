namespace Warehouse.Shipping.Dtos;

/// <summary>A shipment with its lines, as it is read back.</summary>
[MapFrom<Shipment>]
[GenerateProjection]
public partial class ShipmentDto
{
    public Guid Id { get; init; }

    public Guid OrderId { get; init; }

    public string TrackingNumber { get; init; } = "";

    public string? Carrier { get; init; }

    public int Packages { get; init; }

    public ShipmentStatus Status { get; init; }

    public List<ShipmentLineDto> Lines { get; init; } = [];
}
