using Conformance.Sales.Dtos;
using Conformance.Sales.Endpoints;
using Conformance.Sales.Entities;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query.Attributes;

namespace Conformance.Sales.Queries;

/// <summary>
///     The same read, published <b>inside a group</b>.
/// </summary>
/// <remarks>
///     <para>
///         The route declared here is relative: the group's prefix precedes it, so the real address is
///         <c>/api/conformance/orders/{id}</c>. It is the simple half of the case; the other is
///         <c>GetArchivedOrderQuery</c>, inside a nested group, where the prefixes compose.
///     </para>
///     <para>
///         ⚠️ The control that lets the case discriminate is <c>GetOrderQuery</c>, which publishes the
///         <b>same</b> read without a group on <c>api/orders/{id}</c>. Without it, «the prefix is not
///         applied» and «the query does not work» would give the same red.
///     </para>
/// </remarks>
[Query<Order, OrderDto>(Single = true)]
[AllowAnonymous]
[Endpoint(HttpVerb.Get, "/{id}")]
[EndpointGroup<OrdersGroup>]
public partial class GetGroupedOrderQuery
{
    [Filter(MapTo = "PersistenceId")]
    public Guid Id { get; init; }
}
