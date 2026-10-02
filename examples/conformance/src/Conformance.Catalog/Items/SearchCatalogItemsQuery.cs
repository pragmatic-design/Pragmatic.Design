using Conformance.Catalog.Dtos;
using Conformance.Catalog.Entities;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace Conformance.Catalog.Queries;

/// <summary>
///     A read that declares <b>nothing</b>: neither a permission nor <c>[AllowAnonymous]</c>.
/// </summary>
/// <remarks>
///     <para>
///         It exists for a single cell: with the <c>[assembly: PragmaticAutoDerivePermissions]</c>
///         posture on for this module, the route must require the derived name —
///         <c>catalog.catalog-items.search</c> — and refuse an authenticated caller who does not have it.
///         <c>TheDerivedPermissionOnARead</c> measures that.
///     </para>
///     <para>
///         ⚠️ A query is neither an action nor a mutation, and derivation must cover it too: otherwise
///         this route would be published with no requirement, open to anyone authenticated.
///     </para>
///     <para>
///         ⚠️ And the derived name must reach the <b>invoker</b>, not only the route: otherwise this same
///         read would be refused over HTTP and served in-process to anyone calling
///         <c>catalog.SearchCatalogItems(…)</c>. The two doors require the same name, and that is what
///         <c>InProcess_WithoutTheDerivedName_IsRefused</c> and its control measure.
///     </para>
/// </remarks>
[Query<CatalogItem, CatalogItemDto>]
[Endpoint(HttpVerb.Get, "api/catalog-items")]
public partial class SearchCatalogItemsQuery
{
    [Filter(Operator = FilterOperator.Contains)]
    public string? Name { get; init; }
}
