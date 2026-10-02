namespace Showcase.Catalog.Properties.Queries;

/// <summary>
///     Dynamic grid filter for hotel property searches.
///     Demonstrates <c>[GridFilter&lt;T&gt;]</c>: dynamic per-field operator selection for UI grids.
/// </summary>
/// <remarks>
///     Contrast with <see cref="SearchPropertiesQuery" /> (<c>[Query]</c>) which uses
///     compile-time filters, and <see cref="PropertyLocationFilter" /> (<c>[ComplexFilter]</c>)
///     which uses explicit domain-named groups.
///     GridFilter is ideal for data-grid UIs that need operator selection (contains/startsWith/etc).
/// </remarks>
[GridFilter<Property>]
public partial class PropertyGridFilter
{
    /// <summary>One search box across the three text columns.</summary>
    /// <remarks>
    ///     ⚠️ <c>[SearchAcross]</c> is rendered for both <c>Apply</c> and the <c>ToSpecification</c>
    ///     emitted just below it, from one shared expression. A half that looked this property up on
    ///     the entity instead would be a <c>CS1061</c>, and a filter declaring it would not compile at
    ///     all — this declaration is what compiles both halves.
    /// </remarks>
    [SearchAcross(nameof(Property.Name), nameof(Property.City), nameof(Property.Country))]
    public string? Search { get; set; }

    /// <summary>Filter by name — supports Contains, StartsWith, EndsWith.</summary>
    [Filterable(Operators = FilterOps.String)]
    public string? Name { get; set; }

    /// <summary>Dynamic operator for Name.</summary>
    public StringOperator? NameOperator { get; set; }

    [Filterable(Operators = FilterOps.String)]
    public string? City { get; set; }

    public StringOperator? CityOperator { get; set; }

    [Filterable(Operators = FilterOps.String)]
    public string? Country { get; set; }

    public StringOperator? CountryOperator { get; set; }

    /// <summary>Minimum star rating (range filter — uses GreaterOrEqual internally).</summary>
    [Filterable(Operators = FilterOps.Range, MapTo = "StarRating")]
    public int? MinStarRating { get; set; }

    /// <summary>Maximum star rating (range filter — uses LessOrEqual internally).</summary>
    [Filterable(Operators = FilterOps.Range, MapTo = "StarRating")]
    public int? MaxStarRating { get; set; }

    [Filterable]
    public bool? IsActive { get; set; }

    [Sort(DefaultDirection = SortDirection.Ascending, MapTo = "Name")]
    public SortDirection? NameSort { get; set; }

    [Sort(MapTo = "StarRating")]
    public SortDirection? StarRatingSort { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
