using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query.Attributes;
using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Queries;

/// <summary>
///     Reads an order back with its lines.
/// </summary>
/// <remarks>
///     <para>
///         The tests use it as an <b>independent observation</b>: checking a write by reading the write's
///         own response does not prove that anything reached the database.
///     </para>
///     <para>
///         Demonstrates: the <c>Include</c>s come from <c>OrderDto.RequiredNavigations</c> and precede the
///         projection, so <c>Lines</c> comes back filled without this query naming the navigation.
///     </para>
/// </remarks>
[Query<Order, OrderDto>(Single = true)]
[AllowAnonymous]
[Endpoint(HttpVerb.Get, "api/orders/{id}")]
public partial class GetOrderQuery
{
    [Filter(MapTo = "PersistenceId")]
    public Guid Id { get; init; }
}
