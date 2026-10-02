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
    ///     Returns a <see cref="ForbiddenError" /> when a tenant-scoped caller targets a different tenant;
    ///     otherwise <see langword="null" />.
    /// </summary>
    public static IError? CheckTenantBinding(ICurrentUser currentUser, string? targetTenantId, string action)
        => TenantBinding.Permits(currentUser, targetTenantId)
            ? null
            : ForbiddenError.ActionDenied(action, $"tenant:{targetTenantId ?? "global"}");
}
