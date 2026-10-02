using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Samples.Entities;
using Pragmatic.Persistence.Query.Attributes;

namespace Pragmatic.Endpoints.Samples.Queries;

/// <summary>
///     Sample query demonstrating the unified [Query] + [Endpoint] pipeline.
///     Two generators cooperate:
///     <list type="number">
///         <item>Persistence.EFCore generator → generates Apply() method for IPagedQuery&lt;Product&gt;</item>
///         <item>Endpoints generator → generates GET api/products/search endpoint with query parameters</item>
///     </list>
/// </summary>
[Query<Product, Product>]
// Route is relative to the configured RoutePrefix ("/api" in Program.cs) —
// emit only the resource path here, not the prefix. Uses /catalog/products so it
// doesn't shadow /products/{id} when the API versioning middleware is active:
// without this, "search" would bind as {id} on the unversioned fallback route.
[Endpoint(HttpVerb.Get, "/catalog/products")]
public partial class SearchProductsQuery
{
    [Filter]
    public string? Name { get; init; }

    [Filter]
    public string? Category { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;
}
