using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query.Attributes;

namespace Conformance.Sales.Queries;

/// <summary>
///     The sibling read of <c>CreateShipmentReturningIdMutation</c>.
/// </summary>
/// <remarks>
///     A second route for the same entity, on purpose: the <c>Id</c> shape must carry its
///     <c>Location</c> like the default shape, and every Create finds its read from its own route, not
///     from the entity's name.
/// </remarks>
[Query<Shipment, ShipmentDto>(Single = true)]
[AllowAnonymous]
[Endpoint(HttpVerb.Get, "api/shipment-ids/{id}")]
public partial class GetShipmentByIdRouteQuery
{
    public required Guid Id { get; init; }
}
