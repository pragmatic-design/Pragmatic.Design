using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;
using Pragmatic.Actions.Mutation;
using Pragmatic.Authorization;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Entity;

namespace Conformance.Sales.Mutations;

/// <summary>
///     The same protected child, but here the parent declares that it answers for it.
/// </summary>
/// <remarks>
///     <para>
///         It nests <c>GuardDeliveryAddressMutation</c>, which carries <c>[RequirePermission]</c>, exactly
///         like <c>SetGuardedAddressMutation</c>. The only difference is <c>[AbsorbsChildPermissions]</c>,
///         and that difference is what is measured: the two routes are identical in everything else, so
///         what separates them is only the declaration.
///     </para>
///     <para>
///         ⚠️ The direction matters. The default is <b>not</b> to absorb, and rightly: a rule that
///         disappears by omission is the worst shape, because nobody sees it disappear. Here the author
///         writes that this operation answers for what it touches, and whoever reads the type knows it.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[AllowAnonymous]
[AbsorbsChildPermissions]
[Endpoint(HttpVerb.Put, "api/orders/{id}/absorbed-address")]
[ReturnsDto<OrderDto>]
public partial class AbsorbGuardedAddressMutation : Mutation<Order>
{
    public required Guid Id { get; init; }

    public GuardDeliveryAddressMutation? DeliveryAddress { get; init; }
}
