namespace Showcase.Catalog.Amenities.Queries;

/// <summary>
/// Paged search query for amenities.
/// Demonstrates: [Query] + [Endpoint] unified combo, Cacheable.
/// </summary>
[Query<Amenity, AmenityDto>]
[Endpoint(HttpVerb.Get, "api/amenities/search")]
[Cacheable(Duration = "10m", Tags = ["amenities"])]
[RequirePermission(CatalogPermissions.Amenity.Read)]
public partial class SearchAmenitiesQuery
{
    [Filter(Operator = FilterOperator.Contains)]
    public string? Name { get; init; }

    [Filter]
    public AmenityCategory? Category { get; init; }

    /// <summary>
    /// Filter by a set of names (?names=Pool&amp;names=Spa). Collection-of-scalars filter param: binds
    /// repeated query keys and applies SQL IN — exercises the SG collection-filter path.
    /// </summary>
    [Filter(Operator = FilterOperator.In, MapTo = "Name")]
    public List<string>? Names { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? NameSort { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
