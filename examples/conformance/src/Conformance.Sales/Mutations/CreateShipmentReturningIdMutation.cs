using Conformance.Sales.Entities;
using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;

namespace Conformance.Sales.Mutations;

/// <summary>
///     A Create that returns the technical key.
/// </summary>
/// <remarks>
///     <para>
///         Demonstrates: <c>ReturnType = Id</c> answers <b>201</b> with <c>{"id": …}</c> and nothing else,
///         and the boundary member returns the key, not the entity.
///     </para>
///     <para>
///         ⚠️ A declared return shape must be the one on the wire: this case and its siblings fix each
///         shape to what the response really carries.
///     </para>
/// </remarks>
[Mutation(ReturnType = MutationReturnType.Id)]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/shipment-ids")]
public partial class CreateShipmentReturningIdMutation : Mutation<Shipment>
{
    public required string TrackingCode { get; init; }

    public required string Carrier { get; init; }
}
