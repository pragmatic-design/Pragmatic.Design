using Conformance.Catalog.Dtos;
using Conformance.Catalog.Entities;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace Conformance.Catalog.Queries;

/// <summary>
///     The only read in this repository declared <c>[Published]</c>: it joins the boundary's contract,
///     and <b>another</b> boundary executes it by injecting <c>ICatalogReads</c>.
/// </summary>
/// <remarks>
///     <para>
///         It has no <c>[Endpoint]</c>, on purpose: a read contract is not a route. Its user is
///         <c>Conformance.Sales.Mutations.ClassifyOrderMutation</c>, which must enforce a Catalog
///         invariant without touching its module — the dependency goes from Sales to Catalog, and stays
///         acyclic.
///     </para>
///     <para>
///         The contract's method is <c>ICatalogReads.SearchCategories(...)</c>: the class suffix does not
///         enter the name, so the caller does not write
///         <c>SearchCategoriesQuery(new SearchCategoriesQuery { ... })</c>. With
///         <c>[Published(MethodName = "...")]</c> the name is declared.
///     </para>
/// </remarks>
[Query<Category, CategoryDto>]
[Published]
public partial class SearchCategoriesQuery
{
    [Filter(Operator = FilterOperator.Equals)]
    public string? Name { get; init; }
}
