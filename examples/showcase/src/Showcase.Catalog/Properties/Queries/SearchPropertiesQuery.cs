namespace Showcase.Catalog.Properties.Queries;

/// <summary>
/// Paged search query for hotel properties with projection to summary DTO.
/// Demonstrates: [Query] + [Endpoint] unified combo, Cacheable, and [ComplexFilter].
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <c>[CacheKey]</c> is here because the default key is <b>1 200 characters</b>. Every public
/// property contributes <c>Name=value</c>, the complex filter contributes one segment per nested
/// member, and a caller who sets one filter still pays for all of them — on every read and every
/// write, in the key space of whatever backend is behind <c>ICacheStore</c>. Short labels are the
/// only thing that shortens it, because nothing here can be left out: each property changes the
/// answer, sorting and paging included.
/// </para>
/// <para>
/// <c>City</c> goes first: it is the filter this search is actually used with, and a key that
/// opens with it groups an operator's keys by the thing they look for.
/// </para>
/// </remarks>
[Query<Property, PropertySummaryDto>]
[Endpoint(HttpVerb.Get, "api/properties/search")]
[Cacheable(Duration = "5m", Tags = ["properties"])]
[RequirePermission(CatalogPermissions.Property.Read)]
public partial class SearchPropertiesQuery
{
    [Filter(Operator = FilterOperator.Contains)]
    [CacheKey(Name = "n")]
    public string? Name { get; init; }

    [Filter]
    [CacheKey(Name = "city", Order = 0)]
    public string? City { get; init; }

    [Filter]
    [CacheKey(Name = "cc")]
    public string? Country { get; init; }

    [Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = "StarRating")]
    [CacheKey(Name = "min*")]
    public int? MinStarRating { get; init; }

    [Filter(MapTo = "IsActive")]
    [CacheKey(Name = "on")]
    public bool? Active { get; init; }

    /// <summary>
    /// Optional complex location filter (city/country OR group, max star rating).
    /// Sent as JSON query string: <c>?Location={"cityGroup":{"city":"Rome"},"maxStarRating":4}</c>
    /// </summary>
    [ComplexFilter]
    [CacheKey(Name = "loc")]
    public PropertyLocationFilter? Location { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    [CacheKey(Name = "sn")]
    public SortDirection? NameSort { get; init; }

    [Sort]
    [CacheKey(Name = "ss")]
    public SortDirection? StarRatingSort { get; init; }

    [CacheKey(Name = "p")]
    public int Page { get; init; } = 1;

    [CacheKey(Name = "ps")]
    public int PageSize { get; init; } = 20;
}
