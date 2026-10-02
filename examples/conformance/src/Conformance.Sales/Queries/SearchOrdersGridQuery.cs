using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Persistence.Query.Attributes;

namespace Conformance.Sales.Queries;

/// <summary>
///     The grid as a <b>declared read</b>: the canonical request feeds a query.
/// </summary>
/// <remarks>
///     <para>
///         Demonstrates: a property of type <c>GridFilterRequest</c> is not a filter on a column, it is
///         what a grid asks for, and the generated <c>Apply</c> passes it to the entity's bridge
///         (<c>OrderGridFilterBridge.ApplyCanonical</c>): filters, sorts and page together, as the
///         request carries them.
///     </para>
///     <para>
///         ⚠️ The programmatic shape — a <c>[DomainAction]</c> that takes <c>Query()</c> and applies the
///         request by hand — declares nothing: it does not appear in the processing register, does not
///         publish a contract for what the grid can filter, and has no permission on the route.
///     </para>
///     <para>
///         ⚠️ The verb is <c>POST</c> because a canonical request is a nested object and a query string
///         cannot express it: on <c>GET</c> it is <c>PRAG0532</c>. It is the only query of the module
///         that is not a <c>GET</c>, on purpose.
///     </para>
/// </remarks>
[Query<Order, OrderDto>]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/orders/grid")]
public partial class SearchOrdersGridQuery
{
    /// <summary>What the grid asks for, in the canonical form.</summary>
    public GridFilterRequest? Grid { get; init; }
}
