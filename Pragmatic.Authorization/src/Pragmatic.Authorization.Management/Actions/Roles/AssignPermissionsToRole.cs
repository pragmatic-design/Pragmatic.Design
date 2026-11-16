using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Roles;

/// <summary>Assigns permissions to a role (additive — does not remove existing).</summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.Roles.Manage)]
public partial class AssignPermissionsToRole : VoidDomainAction
{
    private IAuthorizationManagementStore _store = null!;
    private ICurrentUser _currentUser = null!;

    // Optional: registered only with a cross-request permission cache, and without one there is
    // nothing cached to evict — the next request resolves from the tables.
    private IPermissionCacheInvalidator? _permissionCache;

    public required string RoleName { get; init; }
    public required string[] Permissions { get; init; }
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
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, tenantId, "assign-permissions-to-role") is { } tenantError)
            return VoidResult<IError>.Failure(tenantError);

        // Holding the role and the assignment permissions together would otherwise let a tenant admin grant
        // themselves anything. An operator with no tenant is the platform, and is not bound by what they hold.
        if (_currentUser.TenantId is { Length: > 0 })
        {
            foreach (var permission in Permissions)
            {
                if (!await _currentUser.Authorization.HasPermissionAsync(permission, ct).ConfigureAwait(false))
                    return VoidResult<IError>.Failure(
                        ForbiddenError.ActionDenied("assign-permissions-to-role", $"permission:{permission}"));
            }
        }

        var now = DateTimeOffset.UtcNow;

        // The role's grants in force in exactly this tenant: a global grant of the same permission is another row.
        var grants = await _store.GetRolePermissionsAsync([RoleName], tenantId, ct).ConfigureAwait(false);
        var existing = grants
            .Where(rp => rp.RoleName == RoleName && rp.TenantId == tenantId && rp.IsInForceAt(now))
            .Select(rp => rp.PermissionName)
            .ToHashSet();

        var added = Permissions
            .Where(permission => existing.Add(permission))
            .Select(permission => new DynamicRolePermission
            {
                RoleName = RoleName,
                PermissionName = permission,
                TenantId = tenantId,
                ValidFrom = now
            })
            .ToList();

        await _store.AddRolePermissionsAsync(added, ct).ConfigureAwait(false);

        await RoleHolders.InvalidateAsync(_store, _permissionCache, RoleName, tenantId, now, ct).ConfigureAwait(false);

        return VoidResult<IError>.Success();
    }
}
