using Pragmatic.Authorization.Policy;
using Pragmatic.Identity;

namespace Pragmatic.Actions.Samples.Authorization;

/// <summary>
///     A named authorization policy used by <c>[RequirePolicy&lt;CanManageOrdersPolicy&gt;]</c>.
///     Demonstrates the <see cref="ResourcePolicy" /> base class evaluated by the
///     <c>PolicyEvaluationFilter</c> (Order 210).
/// </summary>
/// <remarks>
///     Policies are stateless and instantiated once. <see cref="Evaluate" /> returns a simple boolean;
///     it can also be composed fluently, e.g.
///     <c>ResourcePolicy.IsAuthenticated() &amp; ResourcePolicy.RequirePermission("orders.manage")</c>.
/// </remarks>
public sealed class CanManageOrdersPolicy : ResourcePolicy
{
    /// <summary>
    ///     Example rule: only authenticated users may manage orders.
    ///     A real policy would also check permissions/roles via <c>user.Authorization</c>.
    /// </summary>
    public override bool Evaluate(ICurrentUser user) => user.IsAuthenticated;
}
