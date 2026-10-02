using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Entity;
using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Mutations;

/// <summary>
///     The same single navigation <b>without</b> a declared strategy: omitting the child does not touch it.
/// </summary>
/// <remarks>
///     <para>
///         The control case of <c>TheSingleNavigation</c>: inside a mutation the call to
///         <c>MapOneToOne</c> must be guarded, or an update that does not mention the address would
///         detach it anyway. Without this case next to the <c>Detach</c> one, an implementation that
///         always detaches would pass.
///     </para>
///     <para>
///         <b>And it carries the declared group.</b> Its namespace is <c>Conformance.Sales.Mutations</c>,
///         flat, so inference produces nothing and without the attribute it would sit on the root —
///         which makes this the only operation in the repository where <c>[SubBoundary]</c> is
///         observable: the group exists only because someone wrote it.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[SubBoundary(Name = "References", Description = "What an order is called, and who may change it.")]
[AllowAnonymous]
[Endpoint(HttpVerb.Put, "api/orders/{id}/reference")]
[ReturnsDto<OrderDto>]
public partial class RenameOrderMutation : Mutation<Order>
{
    public required Guid Id { get; init; }

    public string Reference { get; init; } = "";

    /// <summary>No <c>[ReferenceStrategy]</c>: it is <c>Merge</c>, and a null says nothing.</summary>
    public WriteDeliveryAddressMutation? DeliveryAddress { get; init; }
}
