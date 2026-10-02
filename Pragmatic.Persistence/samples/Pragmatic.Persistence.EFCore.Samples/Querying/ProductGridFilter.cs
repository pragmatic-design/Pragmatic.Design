using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.Persistence.EFCore.Samples.Entities;

namespace Pragmatic.Persistence.EFCore.Samples.Querying;

/// <summary>
///     A data-grid filter over <see cref="Product"/>. <c>[GridFilter&lt;Product&gt;]</c> makes the SG
///     emit an instance <c>Apply(IQueryable&lt;Product&gt;)</c> that handles per-field operator
///     selection ({Name}Operator), multi-column sort ({Name}Sort), and built-in paging.
/// </summary>
[GridFilter<Product>]
public partial class ProductGridFilter
{
    /// <summary>Name filter — operator chosen at runtime via <see cref="NameOperator"/>.</summary>
    [Filterable(Operators = FilterOps.String)]
    public string? Name { get; set; }

    /// <summary>Dynamic operator for <see cref="Name"/> (Contains / StartsWith / EndsWith).</summary>
    public StringOperator? NameOperator { get; set; }

    [Filterable]
    public bool? IsAvailable { get; set; }

    /// <summary>Sort by price (ascending/descending).</summary>
    [Sort(MapTo = "Price")]
    public SortDirection? PriceSort { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
