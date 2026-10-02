using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Pragmatic.Authorization.Configuration;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Delegation;

/// <summary>
///     Composes the authority of a delegated session from the subject's and the actor's, according to
///     the delegation's own <see cref="DelegationPolicy" />.
/// </summary>
/// <remarks>
///     <para>
///         A decorator, deliberately: every <c>[RequirePermission]</c>, every permission-based query
///         filter and every <c>ResourcePolicy</c> already asks <see cref="IUserAuthorization" />, so
///         all of them inherit the composition without knowing it exists. Making each of them
///         delegation-aware instead would be the same rule written a dozen times, and the twelfth
///         copy is where the hole opens.
///     </para>
///     <para>
///         With no delegation on the session — the overwhelming majority — every member forwards
///         untouched. The class costs one null check.
///     </para>
///     <para>
///         <b>The subject's authority is the inner resolver.</b> <c>ICurrentUser.Id</c> is the subject,
///         so the resolver this decorates already answers for the right person; what it cannot know is
///         that an actor is involved.
///     </para>
/// </remarks>
/// <param name="inner">The subject's own authority.</param>
/// <param name="currentUser">The session, which carries the delegation when there is one.</param>
/// <param name="actorAuthority">Resolves the actor's own authority, when the policy needs it.</param>
/// <param name="options">Cross-tenant and chain-depth limits; the safe defaults apply when absent.</param>
public sealed class DelegatedUserAuthorization(
    IUserAuthorization inner,
    ICurrentUser currentUser,
    IActorAuthorityResolver actorAuthority,
    IOptions<AuthorizationOptions>? options = null) : IUserAuthorization
{
    private IReadOnlySet<string>? _effective;

    /// <summary>
    ///     The delegation on this session, and whether it was admitted.
    /// </summary>
    /// <remarks>
    ///     🔴 A refused delegation must <b>deny</b>, not fall back. Falling back to the subject's own
    ///     authority looks conservative and is the opposite: the request is being made by the actor,
    ///     so handing it the subject's full authority gives it <em>more</em> than the delegation it
    ///     was refused would have. An expired grant would become an upgrade. Three tests caught this
    ///     within a minute of it being written.
    /// </remarks>
    private (IDelegationContext? Context, DelegationRefusal Refusal) Admitted
    {
        get
        {
            var delegation = currentUser.Delegation;
            if (delegation is null)
                return (null, DelegationRefusal.None);

            var opts = options?.Value;
            var refusal = DelegationGuard.Check(
                delegation,
                subjectTenantId: currentUser.TenantId,
                actorTenantId: actorAuthority.ActorTenantId(delegation),
                allowCrossTenant: opts?.AllowCrossTenantDelegation ?? false,
                maxChainDepth: opts?.MaxDelegationChainDepth ?? 2);

            return refusal == DelegationRefusal.None ? (delegation, refusal) : (null, refusal);
        }
    }

    private IDelegationContext? Delegation => Admitted.Context;

    /// <summary>
    ///     Whether the session carries a delegation <b>at all</b> — admitted or refused.
    /// </summary>
    /// <remarks>
    ///     🔴 This, and not "is there an admitted delegation", is what decides whether to forward to
    ///     the inner resolver. Forwarding on refusal hands the actor the subject's full authority,
    ///     which is the fail-open this class exists to prevent. Every forwarding path in this class is a
    ///     place to repeat that mistake: a refused delegation must never reach a path that answers for
    ///     the subject.
    /// </remarks>
    private bool IsDelegated => currentUser.Delegation is not null;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Roles => inner.Roles;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Groups => inner.Groups;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Scopes => inner.Scopes;

    /// <inheritdoc />
    /// <remarks>
    ///     Without a delegation this is the inner set itself, not a copy — the ordinary session pays
    ///     nothing.
    /// </remarks>
    public IReadOnlySet<string> Permissions => _effective ??= Compose();

    /// <inheritdoc />
    /// <remarks>
    ///     🔴 Forwards when there is no delegation, and matches with wildcards when there is. Written
    ///     first as <c>Permissions.Contains(permission)</c>, which is an exact comparison: a caller
    ///     granted <c>catalog.*</c> stopped passing <c>catalog.property.read</c>, and the Showcase
    ///     answered 403 to 274 requests. Restating a matching rule instead of reusing it — the very
    ///     thing the remarks on <see cref="Intersect" /> warn about, done twelve lines above them.
    /// </remarks>
    public bool HasPermission(string permission)
        => IsDelegated ? Grants(permission) : inner.HasPermission(permission);

    /// <inheritdoc />
    public bool HasAnyPermission(IEnumerable<string> permissions)
        => IsDelegated ? permissions.Any(Grants) : inner.HasAnyPermission(permissions);

    /// <inheritdoc />
    public bool HasAllPermissions(IEnumerable<string> permissions)
        => IsDelegated ? permissions.All(Grants) : inner.HasAllPermissions(permissions);

    /// <summary>
    ///     Whether the composed set grants <paramref name="required" />, wildcards included: an
    ///     intersection can legitimately keep <c>billing.*</c>, and comparing it by equality against
    ///     <c>billing.invoice.read</c> would deny a permission the caller has.
    /// </summary>
    private bool Grants(string required)
    {
        var effective = Permissions;
        if (effective.Contains(required))
            return true;

        foreach (var granted in effective)
            if (WildcardMatcher.Matches(granted, required))
                return true;

        return false;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlySet<string>> GetPermissionsAsync(CancellationToken cancellationToken = default)
        => IsDelegated
            ? new ValueTask<IReadOnlySet<string>>(Permissions)
            : inner.GetPermissionsAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default)
        => IsDelegated
            ? new ValueTask<bool>(HasPermission(permission))
            : inner.HasPermissionAsync(permission, cancellationToken);

    /// <inheritdoc />
    public ValueTask<bool> HasAnyPermissionAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default)
        => IsDelegated
            ? new ValueTask<bool>(HasAnyPermission(permissions))
            : inner.HasAnyPermissionAsync(permissions, cancellationToken);

    /// <inheritdoc />
    public ValueTask<bool> HasAllPermissionsAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default)
        => IsDelegated
            ? new ValueTask<bool>(HasAllPermissions(permissions))
            : inner.HasAllPermissionsAsync(permissions, cancellationToken);

    /// <inheritdoc />
    public bool IsInRole(string role) => inner.IsInRole(role);

    /// <inheritdoc />
    public bool IsInGroup(string group) => inner.IsInGroup(group);

    /// <inheritdoc />
    public bool HasScope(string scope) => inner.HasScope(scope);

    private static readonly IReadOnlySet<string> Nothing = new HashSet<string>(StringComparer.Ordinal);

    private IReadOnlySet<string> Compose()
    {
        var (delegation, refusal) = Admitted;

        // Refused: nothing. See the remarks on Admitted — this is the branch that must not become a
        // fallback.
        if (refusal != DelegationRefusal.None)
            return Nothing;

        if (delegation is null)
            return inner.Permissions;

        var subject = inner.Permissions;

        return delegation.Policy switch
        {
            // The subject's authority, whole. The never-delegable list is what protects them here,
            // and it is applied above this class rather than inside it: composition is one job.
            DelegationPolicy.SubjectOnly => subject,

            // Explicit least privilege. An empty allow-list grants nothing, which is the right
            // reading of "a grant that names no permissions".
            DelegationPolicy.GrantScoped => Intersect(subject, actorAuthority.GrantedPermissions(delegation)),

            // Subject ∩ actor, the default.
            _ => Intersect(subject, actorAuthority.ActorPermissions(delegation)),
        };
    }

    /// <summary>
    ///     What both sides allow, at the finest granularity either of them states it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Set intersection would be wrong: <c>billing.*</c> on one side and
    ///         <c>billing.invoice.read</c> on the other share no element, and a legitimate delegation
    ///         would come out silently powerless. <see cref="WildcardMatcher" /> is the same rule the
    ///         resolver applies when checking a single permission — reused rather than restated,
    ///         because two copies of a matching rule is how one of them ends up wrong.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>And it has to be read in both directions.</b> This walked the subject's
    ///         permissions and asked whether the actor granted each, which covers a wildcard held by
    ///         the <em>actor</em> and not one held by the <em>subject</em> — the very case the
    ///         paragraph above describes, implemented for one of its two assignments. A subject whose
    ///         role was granted with <c>WithAllPermissions&lt;TBoundary&gt;()</c> holds
    ///         <c>{boundary}.*</c> and nothing else, so every delegation on their behalf composed to
    ///         an empty authority: the most privileged role in an application was the one that could
    ///         not act through an agent, and nothing said so. Measured on a consumer application,
    ///         where the same delegated call succeeded for a member and was refused for the owner.
    ///     </para>
    ///     <para>
    ///         Walking both sides and keeping what the other grants yields the narrower of the two
    ///         expressed concretely: <c>workspaces.*</c> against <c>workspaces.member.update</c> is
    ///         the update, not the wildcard and not nothing.
    ///     </para>
    /// </remarks>
    private static IReadOnlySet<string> Intersect(IReadOnlySet<string> subject, IReadOnlySet<string> other)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var permission in subject)
        {
            if (Grants(other, permission))
                result.Add(permission);
        }

        foreach (var permission in other)
        {
            if (Grants(subject, permission))
                result.Add(permission);
        }

        return result;
    }

    /// <summary>Whether a set allows a permission, literally or through a wildcard it holds.</summary>
    private static bool Grants(IReadOnlySet<string> granted, string permission)
    {
        if (granted.Contains(permission))
            return true;

        foreach (var pattern in granted)
        {
            if (WildcardMatcher.Matches(pattern, permission))
                return true;
        }

        return false;
    }
}
