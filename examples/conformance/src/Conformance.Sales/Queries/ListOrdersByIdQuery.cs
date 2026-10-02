using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query.Attributes;
using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Queries;

/// <summary>
///     The list form of the same rule: a collection of ids filters the key with <c>In</c>.
/// </summary>
/// <remarks>
///     The mapping belongs to the property, not to <c>Single</c>: a list query that names the entity's
///     <c>Id</c> means the same column as a single one.
/// </remarks>
[Query<Order, OrderDto>]
[AllowAnonymous]
[Endpoint(HttpVerb.Get, "api/orders/by-id")]
public partial class ListOrdersByIdQuery
{
    public List<Guid>? Id { get; init; }
}
