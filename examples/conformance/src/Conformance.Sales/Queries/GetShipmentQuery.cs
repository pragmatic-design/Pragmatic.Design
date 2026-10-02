using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query.Attributes;

namespace Conformance.Sales.Queries;

/// <summary>
///     The sibling read of <c>CreateShipmentMutation</c>: same route, plus the id.
/// </summary>
/// <remarks>
///     It is what produces the 201's <c>Location</c>: the generator knows it at compile time because
///     <c>api/shipments/{id}</c> is exactly <c>api/shipments</c> plus a parameter.
/// </remarks>
[Query<Shipment, ShipmentDto>(Single = true)]
[AllowAnonymous]
[Endpoint(HttpVerb.Get, "api/shipments/{id}")]
public partial class GetShipmentQuery
{
    public required Guid Id { get; init; }
}
