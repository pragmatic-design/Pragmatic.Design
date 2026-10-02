namespace Showcase.Catalog.RoomTypes.Queries;

/// <summary>
/// Search available room types by property and date range.
/// Demonstrates: [Query] + [Endpoint] unified combo, Cacheable with short TTL.
/// </summary>
[Query<RoomType, RoomTypeSummaryDto>]
[Endpoint(HttpVerb.Get, "api/rooms/available")]
[Cacheable(Duration = "1m", Tags = ["room-types"])]
[RequirePermission(CatalogPermissions.RoomType.Read)]
public partial class SearchAvailableRoomsQuery
{
    [Filter]
    public Guid? PropertyId { get; init; }

    [Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = "MaxOccupancy")]
    public int? MinOccupancy { get; init; }

    [Filter(Operator = FilterOperator.LessOrEqual, MapTo = "BaseRate")]
    public decimal? MaxRate { get; init; }

    [Filter(Operator = FilterOperator.Contains)]
    public string? Name { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? BaseRateSort { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
