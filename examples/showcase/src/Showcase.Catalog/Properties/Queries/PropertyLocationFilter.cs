namespace Showcase.Catalog.Properties.Queries;

/// <summary>
///     Complex location filter for property search.
///     Sent as a JSON query string parameter: <c>?Location={"cityGroup":{"city":"Rome"}}</c>.
/// </summary>
/// <remarks>
///     Demonstrates <c>[ComplexFilter]</c>: the source generator applies
///     <c>PropertyLocationFilterExtensions.ApplyFilter()</c> inside the generated <c>Apply()</c> method.
/// </remarks>
[FilterDto<Property>]
public partial class PropertyLocationFilter
{
    /// <summary>Match city OR country (OR group).</summary>
    [FilterGroup(FilterLogic.Or)]
    public CityGroupFilter? CityGroup { get; set; }

    /// <summary>Maximum price per night (applies as &lt;= filter on PricePerNight).</summary>
    [Filter(Operator = FilterOperator.LessOrEqual, MapTo = "StarRating")]
    public int? MaxStarRating { get; set; }
}
