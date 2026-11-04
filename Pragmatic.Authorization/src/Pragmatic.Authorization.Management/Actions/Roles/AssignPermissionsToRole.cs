using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Identity;
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

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, TenantId, "assign-permissions-to-role") is { } tenantError)
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
        var grants = await _store.GetRolePermissionsAsync([RoleName], TenantId, ct).ConfigureAwait(false);
        var existing = grants
            .Where(rp => rp.RoleName == RoleName && rp.TenantId == TenantId && rp.IsInForceAt(now))
            .Select(rp => rp.PermissionName)
            .ToHashSet();

        var added = Permissions
            .Where(permission => existing.Add(permission))
            .Select(permission => new DynamicRolePermission
            {
                RoleName = RoleName,
                PermissionName = permission,
                TenantId = TenantId,
                ValidFrom = now
            })
            .ToList();

        await _store.AddRolePermissionsAsync(added, ct).ConfigureAwait(false);

        // The cache tags a user's claim roles, not the roles this package assigns, so the users who
        // hold this role here are found and evicted one by one.
        if (_permissionCache is not null)
        {
            var holders = (await _store.GetRoleHoldersAsync(RoleName, ct).ConfigureAwait(false))
                .Where(a => (TenantId == null || a.TenantId == null || a.TenantId == TenantId)
                            && (a.ValidTo == null || a.ValidTo > now))
                .Select(a => a.UserId)
                .Distinct();

            foreach (var holder in holders)
                await _permissionCache.InvalidateUserAsync(holder, ct).ConfigureAwait(false);
        }

        return VoidResult<IError>.Success();
    }
}
