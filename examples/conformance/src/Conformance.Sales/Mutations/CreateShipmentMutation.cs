using Conformance.Sales.Entities;
using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;

namespace Conformance.Sales.Mutations;

/// <summary>
///     A Create that declares no <c>ReturnType</c>.
/// </summary>
/// <remarks>
///     <para>
///         Demonstrates: on the wire a Create that declares nothing answers with the id of the written
///         row, never with the entity; in-process, its boundary member returns the attribute's default,
///         <c>MutationAttribute.ReturnType</c> (<c>Entity</c>). <c>TheCreatedResponse</c> measures both
///         halves.
///     </para>
///     <para>
///         The sibling read <c>GetShipmentQuery</c> sits on <c>api/shipments/{id}</c>, so the 201
///         carries a <c>Location</c> that names it.
///     </para>
/// </remarks>
[Mutation]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/shipments")]
public partial class CreateShipmentMutation : Mutation<Shipment>
{
    public required string TrackingCode { get; init; }

    public required string Carrier { get; init; }
}
