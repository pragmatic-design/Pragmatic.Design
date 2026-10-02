using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;

namespace Conformance.Sales.Mutations;

/// <summary>
///     An anonymous composite whose second step declares a permission.
/// </summary>
/// <remarks>
///     <para>
///         It exists for one question: <b>does a step's <c>[RequirePermission]</c> hold?</b> The
///         composite is <c>[AllowAnonymous]</c>, the step is not, and the caller has no permission. If
///         the response came back 2xx with the row written, a permission declared on a mutation would
///         stop holding as soon as someone used it as a step — with nothing saying so.
///     </para>
///     <para>
///         ⚠️ The default is the same as the <b>neighbouring</b> mechanism: a child nested inside a
///         mutation keeps its own permission, and the parent must declare
///         <c>[AbsorbsChildPermissions]</c> to answer in its place. A composite that absorbs its steps'
///         permissions declares it too — <c>AbsorbingStepCompositeAction</c> is that other half.
///     </para>
/// </remarks>
[DomainAction]
[CompositeAction]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/orders/guarded-steps")]
public partial class GuardedStepCompositeAction : VoidDomainAction
{
    /// <summary>The harmless step: without it, the case could not tell «refused» from «does nothing».</summary>
    public required RenameDeliveryAddressMutation Rename { get; init; }

    /// <summary>The step that declares <c>conformance.address.guard</c>.</summary>
    public required GuardDeliveryAddressMutation Guarded { get; init; }
}
