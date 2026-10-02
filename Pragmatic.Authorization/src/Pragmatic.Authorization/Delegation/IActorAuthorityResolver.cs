using System.Collections.Generic;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Delegation;

/// <summary>
///     Answers what the acting party is allowed, so the composition can bound the subject's authority
///     by it.
/// </summary>
/// <remarks>
///     Separate from <see cref="IUserAuthorization" /> because that one answers for the <em>current</em>
///     session, and under delegation the current session is the subject. The actor's own authority has
///     to be resolved out of band — from its service identity, from the grant, or from wherever the
///     application keeps it.
/// </remarks>
public interface IActorAuthorityResolver
{
    /// <summary>What the actor is allowed in its own right. Used by <see cref="DelegationPolicy.Intersection" />.</summary>
    IReadOnlySet<string> ActorPermissions(IDelegationContext delegation);

    /// <summary>
    ///     What the grant explicitly names. Used by <see cref="DelegationPolicy.GrantScoped" />, and
    ///     the reason that policy exists: a job's service identity holds no user-level permissions, so
    ///     intersecting with it would leave nothing.
    /// </summary>
    IReadOnlySet<string> GrantedPermissions(IDelegationContext delegation);

    /// <summary>
    ///     The tenant the actor belongs to, so a delegation across a tenant boundary can be refused.
    /// </summary>
    /// <remarks>
    ///     <c>null</c> means "same tenant as the subject" — the single-tenant case, and the honest
    ///     answer for a resolver that has no notion of tenancy.
    /// </remarks>
    string? ActorTenantId(IDelegationContext delegation) => null;
}
