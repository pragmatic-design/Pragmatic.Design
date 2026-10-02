using Pragmatic.Actions.Samples.Entities;
using Pragmatic.Persistence.Query.Attributes;

namespace Pragmatic.Actions.Samples.Queries;

/// <summary>
///     Sample demonstrating [Query] source generation.
///     The [Query] attribute generates:
///     <list type="number">
///         <item>Apply() method implementing IPagedQuery&lt;Order&gt; (Persistence generator)</item>
///     </list>
///     To expose as an endpoint, add [Endpoint] from Pragmatic.Endpoints.
/// </summary>
[Query<Order>]
public partial class SearchOrdersQuery
{
    [Filter]
    public string? Product { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
