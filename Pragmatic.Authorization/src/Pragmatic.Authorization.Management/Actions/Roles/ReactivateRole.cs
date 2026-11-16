using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Roles;

/// <summary>
///     Reactivates a role <see cref="DeactivateRole" /> deactivated (#725): its grants and assignments, kept meanwhile, take effect again
///     from the next request.
/// </summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.Roles.Manage)]
public partial class ReactivateRole : VoidDomainAction<NotFoundError>
{
    private IAuthorizationManagementStore _store = null!;
    private ICurrentUser _currentUser = null!;

    // Optional: registered only with a cross-request permission cache, and without one there is
    // nothing cached to evict — the next request resolves from the tables.
    private IPermissionCacheInvalidator? _permissionCache;

    public required string RoleName { get; init; }
    public string? TenantId { get; init; }

    /// <summary>
    ///     The request's tenant, written by the invoker: over HTTP a body never carries <see cref="TenantId" />, so this is the scope for a
    ///     caller bound to a tenant (#778).
    /// </summary>
    [FromTenant]
    public string? RequestTenantId { get; private set; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        var tenantId = TenantScopeGuard.Scope(_currentUser, RequestTenantId, TenantId);
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, tenantId, "reactivate-role") is { } tenantError)
            return VoidResult<IError>.Failure(tenantError);

        var role = RoleActivity.InScope(await _store.GetRolesAsync(tenantId, ct).ConfigureAwait(false), RoleName, tenantId);
        if (role is null)
            return VoidResult<IError>.Failure(NotFoundError.Create("DynamicRole", RoleName));

        var now = DateTimeOffset.UtcNow;
        if (role.DeactivatedAt is not null)
        {
            role.DeactivatedAt = null;
            role.UpdatedAt = now;
            role.UpdatedBy = _currentUser.Id;
            await _store.UpdateRoleAsync(role, ct).ConfigureAwait(false);
        }

        await RoleHolders.InvalidateAsync(_store, _permissionCache, RoleName, tenantId, now, ct).ConfigureAwait(false);
        return VoidResult<IError>.Success();
    }
}
