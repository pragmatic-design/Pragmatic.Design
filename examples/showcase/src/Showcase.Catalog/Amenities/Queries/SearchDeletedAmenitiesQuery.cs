namespace Showcase.Catalog.Amenities.Queries;

/// <summary>
///     Admin query that bypasses all query filters for Amenity to find deleted amenities.
///     Demonstrates: [WithoutFilter&lt;TEntity&gt;] for declarative filter override.
/// </summary>
[Query<Amenity, AmenityDto>]
[Endpoint(HttpVerb.Get, "api/amenities/deleted")]
[WithoutFilter<Amenity>]
[RequirePermission(CatalogPermissions.Amenity.Delete)]
public partial class SearchDeletedAmenitiesQuery
{
    [Filter(Operator = FilterOperator.Contains)]
    public string? Name { get; init; }

    [Filter]
    public AmenityCategory? Category { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
