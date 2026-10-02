using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;
using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Entity;

namespace Conformance.Sales.Mutations;

/// <summary>
///     The parent that nests a child protected by a permission.
/// </summary>
/// <remarks>
///     <para>
///         The operation itself is anonymous — <c>[AllowAnonymous]</c> — and the child is not. It is
///         exactly the shape that makes the question measurable: does the child's permission hold when it
///         is reached from the parent? If it did not, this route would be a shortcut around a rule the
///         child declares.
///     </para>
///     <para>
///         ⚠️ The case's control is <c>SetDeliveryAddressMutation</c>, which writes the same entity with a
///         child <b>without</b> a permission and must keep passing: without it, a check applied to every
///         nested write — one that refused everything — would be indistinguishable from one that works.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[AllowAnonymous]
[Endpoint(HttpVerb.Put, "api/orders/{id}/guarded-address")]
[ReturnsDto<OrderDto>]
public partial class SetGuardedAddressMutation : Mutation<Order>
{
    public required Guid Id { get; init; }

    public GuardDeliveryAddressMutation? DeliveryAddress { get; init; }
}
