using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Mapping.Mutation;
using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Mutations;

/// <summary>
///     The single navigation, with the strategy declared: <b>a null removes the address</b>.
/// </summary>
/// <remarks>
///     <para>
///         Without a declared strategy «remove the address» cannot be expressed: the generated code
///         wraps the call in <c>if (this.X is not null)</c>, so the null never reaches
///         <c>MapOneToOne</c>. <c>[ReferenceStrategy(ReferenceStrategy.Detach)]</c> lets it through.
///     </para>
///     <para>
///         ⚠️ Both doors — a <c>[MapTo]</c> DTO and a <b>mutation</b> — must agree: without the guard in
///         a mutation, an update that omits an optional child would <b>remove its link</b>, silently.
///         Both mean <c>Merge</c>, and removing is declared. <c>TheSingleNavigation</c> measures both
///         halves.
///     </para>
///     <para>
///         <c>Reference</c> is here so that the body is an object: with a single body property the body
///         <b>is</b> that property, and there would be no way to send the null this case must send.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[AllowAnonymous]
[Endpoint(HttpVerb.Put, "api/orders/{id}/delivery-address")]
[ReturnsDto<OrderDto>]
public partial class SetDeliveryAddressMutation : Mutation<Order>
{
    public required Guid Id { get; init; }

    public string Reference { get; init; } = "";

    /// <summary>The address, or <c>null</c> to say the order no longer has one.</summary>
    [ReferenceStrategy(ReferenceStrategy.Detach)]
    public WriteDeliveryAddressMutation? DeliveryAddress { get; init; }
}
