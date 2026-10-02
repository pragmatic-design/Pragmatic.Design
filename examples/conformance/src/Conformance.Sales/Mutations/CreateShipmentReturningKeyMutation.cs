using Conformance.Sales.Entities;
using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;

namespace Conformance.Sales.Mutations;

/// <summary>
///     A Create that returns the domain key.
/// </summary>
/// <remarks>
///     <para>
///         Demonstrates: <c>ReturnType = LogicalKey</c> answers <b>201</b> with the parts of the
///         <c>[LogicKey]</c> under their wire name, and nothing else.
///     </para>
///     <para>
///         ⚠️ It is also the <c>Location</c> control: no read answers on <c>api/shipment-keys/{id}</c>,
///         so the 201 carries none. An invented address would be worse than
///         none.
///     </para>
/// </remarks>
[Mutation(ReturnType = MutationReturnType.LogicalKey)]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/shipment-keys")]
public partial class CreateShipmentReturningKeyMutation : Mutation<Shipment>
{
    public required string TrackingCode { get; init; }

    public required string Carrier { get; init; }
}
