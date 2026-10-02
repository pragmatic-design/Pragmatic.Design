namespace Showcase.Catalog.Dtos;

/// <summary>
/// Room type summary for list views.
/// Demonstrates: MapFrom, GenerateProjection, MapProperty (flattening navigation).
/// </summary>
[MapFrom<RoomType>]
[GenerateProjection]
public partial class RoomTypeSummaryDto
{
    public Guid Id { get; init; }
    public Guid PropertyId { get; init; }
    public string Name { get; init; } = "";
    public string Code { get; init; } = "";
    public int MaxOccupancy { get; init; }
    public decimal BaseRate { get; init; }
    public string Currency { get; init; } = "";
    public int TotalRooms { get; init; }

    /// <summary>Flattened from RoomType.Property.Name. Safe for EF projection.</summary>
    [MapProperty("Property.Name")]
    public string PropertyName { get; init; } = "";
}
