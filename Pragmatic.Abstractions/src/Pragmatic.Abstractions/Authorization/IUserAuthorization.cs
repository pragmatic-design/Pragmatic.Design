using System.Threading;
using System.Threading.Tasks;

namespace Pragmatic.Authorization;

/// <summary>
///     Provides authorization information for the current user.
///     Accessed via <c>ICurrentUser.Authorization</c> — separates authorization
///     concerns from identity (authentication) concerns.
/// </summary>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface IUserAuthorization
{
    /// <summary>Roles assigned to the user.</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>Resolved permissions for the user (expanded, cached per request).</summary>
    IReadOnlySet<string> Permissions { get; }

    /// <summary>
    ///     Whether this authorization is in the middle of resolving its own permissions.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An <c>IPermissionProvider</c> may read the application database — a per-tenant
    ///         permission store has to. That read goes through a repository, a permission-based query
    ///         filter asks the caller's permissions, and resolution starts again. The resolver's guard
    ///         breaks the cycle by handing back an empty set, so nothing crashes; but a read filtered
    ///         by permissions that are not known yet is a wrong read.
    ///     </para>
    ///     <para>
    ///         ⚠️ "Read past the filter pipeline" is a trap as an instruction:
    ///         <c>DisableAll()</c> does it, while <c>Disable&lt;TFilter&gt;()</c> lifts <b>one</b>
    ///         filter and leaves every other permission filter on the entity resolving — and a
    ///         provider cannot name filters it has never heard of. The correct-looking narrowing is
    ///         the one that breaks. The pipeline reads this flag instead, so no lift is needed.
    ///     </para>
    ///     <para>
    ///         Defaults to <c>false</c>: an implementation that does not resolve anything is never in
    ///         the middle of doing so.
    ///     </para>
    /// </remarks>
    bool IsResolvingPermissions => false;

    /// <summary>Groups the user belongs to (e.g., departments, teams).</summary>
    IReadOnlyCollection<string> Groups { get; }

    /// <summary>OAuth/OIDC scopes granted to the current token.</summary>
    IReadOnlyCollection<string> Scopes { get; }

    /// <summary>Checks whether the user has a specific permission.</summary>
    bool HasPermission(string permission);

    /// <summary>Checks whether the user has at least one of the specified permissions (OR logic).</summary>
    bool HasAnyPermission(IEnumerable<string> permissions);

    /// <summary>Checks whether the user has all of the specified permissions (AND logic).</summary>
    bool HasAllPermissions(IEnumerable<string> permissions);

    // ── Async variants ──────────────────────────────────────────────────────────────────────────
    // Permission resolution can hit a DB/cache-backed store. The synchronous members above block a
    // thread on a cache miss; in an async request pipeline prefer these. Default implementations just
    // delegate to the synchronous members (so existing implementers don't break) — a resolver backed
    // by an async store overrides them to actually await instead of blocking.

    /// <summary>Resolves the user's permissions asynchronously (awaits the store instead of blocking).</summary>
    ValueTask<IReadOnlySet<string>> GetPermissionsAsync(CancellationToken cancellationToken = default)
        => new(Permissions);

    /// <summary>Async <see cref="HasPermission"/> — awaits resolution instead of blocking a thread.</summary>
    ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default)
        => new(HasPermission(permission));

    /// <summary>Async <see cref="HasAnyPermission"/> (OR).</summary>
    ValueTask<bool> HasAnyPermissionAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default)
        => new(HasAnyPermission(permissions));

    /// <summary>Async <see cref="HasAllPermissions"/> (AND).</summary>
    ValueTask<bool> HasAllPermissionsAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default)
        => new(HasAllPermissions(permissions));

    /// <summary>Checks whether the user belongs to the specified role.</summary>
    bool IsInRole(string role);

    /// <summary>Checks whether the user belongs to the specified group.</summary>
    bool IsInGroup(string group);

    /// <summary>Checks whether the current token has the specified OAuth/OIDC scope.</summary>
    bool HasScope(string scope);
}
