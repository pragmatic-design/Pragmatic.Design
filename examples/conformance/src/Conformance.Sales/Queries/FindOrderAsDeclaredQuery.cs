using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query.Attributes;
using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Queries;

/// <summary>
///     A get-by-id written the way an update mutation is: a required <c>Id</c>, and nothing else.
/// </summary>
/// <remarks>
///     The entity's <c>Id</c> is the generated alias <c>Id => PersistenceId</c>, unmapped. The mutation
///     translates it on its own, and so does the query: a <c>Where</c> on the unmapped alias would not
///     translate. <c>GetOrderQuery</c> keeps the explicit <c>[Filter(MapTo = "PersistenceId")]</c>
///     form, which must still work.
/// </remarks>
[Query<Order, OrderDto>(Single = true)]
[AllowAnonymous]
[Endpoint(HttpVerb.Get, "api/orders/{id}/as-declared")]
public partial class FindOrderAsDeclaredQuery
{
    public required Guid Id { get; init; }
}
