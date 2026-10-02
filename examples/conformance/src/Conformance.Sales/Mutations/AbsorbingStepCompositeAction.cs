using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;

namespace Conformance.Sales.Mutations;

/// <summary>
///     The same composite, which however declares that it answers for its steps' permissions.
/// </summary>
/// <remarks>
///     <para>
///         The pair with <c>GuardedStepCompositeAction</c> is what makes the case a measure: the same
///         two steps, the same caller without permissions, and the only difference is this line. Without
///         the second, «the step's permission holds» would also be satisfied by a check that always
///         refuses.
///     </para>
///     <para>
///         ⚠️ Absorbing is the same word a mutation uses for its nested children, and it is the same
///         thing: the composite answers in place of the steps, and whoever reads it sees it written.
///     </para>
/// </remarks>
[DomainAction]
[CompositeAction]
[AbsorbsChildPermissions]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/orders/absorbing-steps")]
public partial class AbsorbingStepCompositeAction : VoidDomainAction
{
    /// <summary>The harmless step.</summary>
    public required RenameDeliveryAddressMutation Rename { get; init; }

    /// <summary>The step that declares <c>conformance.address.guard</c>, absorbed here.</summary>
    public required GuardDeliveryAddressMutation Guarded { get; init; }
}
