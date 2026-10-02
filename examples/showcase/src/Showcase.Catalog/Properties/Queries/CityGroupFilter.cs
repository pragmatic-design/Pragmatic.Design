namespace Showcase.Catalog.Properties.Queries;

/// <summary>
///     Nested filter group matching city or country.
///     Used inside <see cref="PropertyLocationFilter" /> with OR logic.
/// </summary>
[FilterDto<Property>]
public partial class CityGroupFilter
{
    [Filter(Operator = FilterOperator.Contains)]
    public string? City { get; set; }

    [Filter(Operator = FilterOperator.Contains)]
    public string? Country { get; set; }
}
