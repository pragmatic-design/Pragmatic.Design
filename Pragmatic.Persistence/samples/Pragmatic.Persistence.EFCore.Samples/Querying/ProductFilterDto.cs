using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.Persistence.EFCore.Samples.Entities;

namespace Pragmatic.Persistence.EFCore.Samples.Querying;

/// <summary>
///     A filter DTO over <see cref="Product"/>. <c>[FilterDto&lt;Product&gt;]</c> makes the SG emit
///     <c>ProductFilterDtoExtensions.ApplyFilter(query, filter)</c>. Null properties are skipped,
///     so one DTO covers many optional-criteria combinations.
/// </summary>
[FilterDto<Product>]
public partial class ProductFilterDto
{
    /// <summary>Substring match on the product name (Contains).</summary>
    [Filter(Operator = FilterOperator.Contains)]
    public string? Name { get; init; }

    /// <summary>Exact availability match.</summary>
    [Filter]
    public bool? IsAvailable { get; init; }

    /// <summary>Maximum price (maps to Price with a &lt;= comparison).</summary>
    [Filter(Operator = FilterOperator.LessOrEqual, MapTo = "Price")]
    public decimal? MaxPrice { get; init; }
}
