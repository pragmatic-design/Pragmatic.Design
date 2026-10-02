using Microsoft.EntityFrameworkCore;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Roles;

/// <summary>Assigns permissions to a role (additive — does not remove existing).</summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.Roles.Manage)]
public partial class AssignPermissionsToRole : VoidDomainAction
{
    private DbContext _dbContext = null!;
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

        // Get existing active assignments for this role
        var existing = await _dbContext.Set<DynamicRolePermission>()
            .Where(rp => rp.RoleName == RoleName
                         && (rp.TenantId == TenantId)
                         && rp.ValidFrom <= now
                         && (rp.ValidTo == null || rp.ValidTo > now))
            .Select(rp => rp.PermissionName)
            .ToHashSetAsync(ct)
            .ConfigureAwait(false);

        foreach (var permission in Permissions)
        {
            if (existing.Contains(permission))
                continue;

            _dbContext.Set<DynamicRolePermission>().Add(new DynamicRolePermission
            {
                RoleName = RoleName,
                PermissionName = permission,
                TenantId = TenantId,
                ValidFrom = now
            });
        }

        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        // The cache tags a user's claim roles, not the roles this package assigns, so the users who
        // hold this role here are found and evicted one by one.
        if (_permissionCache is not null)
        {
            var holders = await _dbContext.Set<UserRoleAssignment>()
                .Where(a => a.RoleName == RoleName
                            && (TenantId == null || a.TenantId == null || a.TenantId == TenantId)
                            && (a.ValidTo == null || a.ValidTo > now))
                .Select(a => a.UserId)
                .Distinct()
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var holder in holders)
                await _permissionCache.InvalidateUserAsync(holder, ct).ConfigureAwait(false);
        }

        return VoidResult<IError>.Success();
    }
}
