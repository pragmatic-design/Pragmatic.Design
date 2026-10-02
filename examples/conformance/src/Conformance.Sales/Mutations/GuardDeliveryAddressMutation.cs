using Conformance.Sales.Entities;
using Pragmatic.Actions.Mutation;
using Pragmatic.Authorization;
using Pragmatic.Validation.Attributes;

namespace Conformance.Sales.Mutations;

/// <summary>
///     A child that requires a permission.
/// </summary>
/// <remarks>
///     <para>
///         It writes the same entity as <c>WriteDeliveryAddressMutation</c>, which <c>DeliveryAddress</c>
///         allows because it is <c>[PartOf&lt;Order&gt;(Exclusive = false)]</c>. It exists for one reason:
///         to measure that a child's <c>[RequirePermission]</c> holds also when the child is reached
///         through the parent.
///     </para>
///     <para>
///         ⚠️ A mutation child has no door of its own — no <c>[Endpoint]</c> — so the parent is the
///         <b>only</b> way to reach it. If the permission did not hold there, it would hold nowhere: a
///         rule written and never applied.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission("conformance.address.guard")]
public partial class GuardDeliveryAddressMutation : Mutation<DeliveryAddress>
{
    public Guid Id { get; init; }

    /// <summary>
    ///     A rule next to the permission, to measure which of the two speaks first.
    /// </summary>
    /// <remarks>
    ///     A body that violates this rule, sent by someone without the permission, must be refused for
    ///     the permission and not for the rule: the order is decided in the invoker, and without a rule
    ///     on the protected child no case could observe it.
    /// </remarks>
    [NotEmpty]
    public string Street { get; init; } = "";

    public string City { get; init; } = "";
}
