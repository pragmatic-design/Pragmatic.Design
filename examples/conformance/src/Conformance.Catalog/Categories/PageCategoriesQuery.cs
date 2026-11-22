using Conformance.Catalog.Dtos;
using Conformance.Catalog.Entities;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query.Attributes;

namespace Conformance.Catalog.Queries;

/// <summary>
///     A paged query that declares no <c>[Sort]</c>: the shape an application writes for a plain list.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Paging without an order is not stable: two requests for the same page can return different rows,
///         and a row can land on two pages or on none. The generated query orders by the entity's key when
///         nothing else orders it (#938); <c>APagedQueryWithoutASort</c> reads it on the SQL sent.
///     </para>
///     <para>
///         Over <c>Category</c>, whose DTO has no collection: a projected collection makes EF Core order by the
///         key on its own, and the case would pass with or without the fix.
///     </para>
/// </remarks>
[Query<Category, CategoryDto>(Paged = true)]
[AllowAnonymous]
[Endpoint(HttpVerb.Get, "api/categories/paged")]
public partial class PageCategoriesQuery;
