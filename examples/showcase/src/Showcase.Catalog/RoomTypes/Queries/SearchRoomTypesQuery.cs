namespace Showcase.Catalog.RoomTypes.Queries;

/// <summary>
/// Paged search query for room types within a property.
/// Demonstrates: [Query] + [Endpoint] unified combo, Cacheable.
/// </summary>
[Query<RoomType, RoomTypeSummaryDto>]
[Endpoint(HttpVerb.Get, "api/room-types/search")]
[Cacheable(Duration = "5m", Tags = ["room-types"])]
[RequirePermission(CatalogPermissions.RoomType.Read)]
public partial class SearchRoomTypesQuery
{
    [Filter]
    public Guid? PropertyId { get; init; }

    [Filter(Operator = FilterOperator.Contains)]
    public string? Name { get; init; }

    [Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = "MaxOccupancy")]
    public int? MinOccupancy { get; init; }

    [Filter(Operator = FilterOperator.LessOrEqual, MapTo = "BaseRate")]
    public decimal? MaxBaseRate { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? BaseRateSort { get; init; }

    [Sort]
    public SortDirection? NameSort { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
