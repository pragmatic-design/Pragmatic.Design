using Conformance.Sales.Dtos;
using Conformance.Sales.Endpoints;
using Conformance.Sales.Entities;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query.Attributes;

namespace Conformance.Sales.Queries;

/// <summary>
///     The read inside a <b>nested</b> group: the prefixes compose.
/// </summary>
/// <remarks>
///     <para>
///         <c>ArchivedOrdersGroup</c> declares <c>/archived</c> and sits inside <c>OrdersGroup</c>, which
///         declares <c>/api/conformance/orders</c>. The real address is therefore
///         <c>/api/conformance/orders/archived/{id}</c>, and neither declaration writes it in full — the
///         composition produces it, and that is what is under measure.
///     </para>
///     <para>
///         ⚠️ It is the case that exercises nesting in a running application, which a generator snapshot
///         cannot do.
///     </para>
/// </remarks>
[Query<Order, OrderDto>(Single = true)]
[AllowAnonymous]
[Endpoint(HttpVerb.Get, "/{id}")]
[EndpointGroup<ArchivedOrdersGroup>]
public partial class GetArchivedOrderQuery
{
    [Filter(MapTo = "PersistenceId")]
    public Guid Id { get; init; }
}
