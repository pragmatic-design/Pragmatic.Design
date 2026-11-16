using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management;

/// <summary>
///     Enforces that a tenant-scoped caller may only operate within its own tenant — not another, and not
///     the global level. A caller with no tenant (global admin) is unrestricted. The rule itself is
///     <see cref="TenantBinding" />; this turns its answer into the error the actions return.
/// </summary>
/// <remarks>
///     Without this guard the flat management permissions (e.g. <c>authorization.roles.manage</c>) would
///     let a tenant-A admin target tenant B simply by passing a different <c>TenantId</c>. Every management
///     action that accepts a <c>TenantId</c> must call this before touching data, so the binding is enforced
///     uniformly rather than on a single action.
/// </remarks>
internal static class TenantScopeGuard
{
    /// <summary>
    ///     The tenant an action acts on: the one it names, else, for a caller bound to a tenant, the tenant the request resolved
    ///     (<paramref name="requestTenant" />, the action's <c>[FromTenant]</c> property). A caller bound to no tenant naming none acts on
    ///     the global level.
    /// </summary>
    /// <remarks>
    ///     The serializer removes <c>TenantId</c> from every body (<c>ReservedWireNames</c>), so over HTTP an exposed action never names a
    ///     tenant, and before #778 it read "global": a tenant administrator was refused, and a platform operator acted globally whatever it
    ///     sent. The request's tenant is not trusted on its own: <see cref="CheckTenantBinding" /> still refuses a tenant that is not the
    ///     caller's. A tenant-bound caller naming none used to be refused as acting globally, so reading that case as the request's tenant
    ///     takes nothing away that was allowed.
    /// </remarks>
    public static string? Scope(ICurrentUser currentUser, string? requestTenant, string? named)
        => named is { Length: > 0 } || currentUser.TenantId is not { Length: > 0 } || requestTenant is not { Length: > 0 }
            ? named
            : requestTenant;

    /// <summary>
    ///     Returns a <see cref="ForbiddenError" /> when a tenant-scoped caller targets a different tenant;
    ///     otherwise <see langword="null" />.
    /// </summary>
    public static IError? CheckTenantBinding(ICurrentUser currentUser, string? targetTenantId, string action)
        => TenantBinding.Permits(currentUser, targetTenantId)
            ? null
            : ForbiddenError.ActionDenied(action, $"tenant:{targetTenantId ?? "global"}");
}
