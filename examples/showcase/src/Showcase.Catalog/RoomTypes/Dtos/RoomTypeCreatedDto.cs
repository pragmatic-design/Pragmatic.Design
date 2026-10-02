namespace Showcase.Catalog.Dtos;

/// <summary>
/// A room type as the create endpoint answers with it.
/// </summary>
/// <remarks>
/// Same fields as <see cref="RoomTypeSummaryDto"/> minus the flattened <c>Property.Name</c>. A write
/// answers from the entity it just persisted, whose reference navigations were never loaded, and
/// <c>FromEntity</c> walks <c>entity.Property.Name</c> straight through — so a DTO built for a query
/// projection is not automatically safe as a write response. The read path is unaffected: there the
/// flattening becomes a join.
/// </remarks>
[MapFrom<RoomType>]
public partial class RoomTypeCreatedDto
{
    public Guid Id { get; init; }
    public Guid PropertyId { get; init; }
    public string Name { get; init; } = "";
    public string Code { get; init; } = "";
    public int MaxOccupancy { get; init; }
    public decimal BaseRate { get; init; }
    public string Currency { get; init; } = "";
    public int TotalRooms { get; init; }
}
