using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Assignments;

/// <summary>Assigns a role to a user with optional temporal validity.</summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.Assignments.Manage)]
public partial class AssignRoleToUser : DomainAction<Guid>
{
    private IAuthorizationManagementStore _store = null!;
    private ICurrentUser _currentUser = null!;

    // Optional: registered only with a cross-request permission cache, and without one there is
    // nothing cached to evict — the next request resolves from the tables.
    private IPermissionCacheInvalidator? _permissionCache;

    public required string UserId { get; init; }
    public required string RoleName { get; init; }
    public string? TenantId { get; init; }
    public DateTimeOffset? ValidFrom { get; init; }
    public DateTimeOffset? ValidTo { get; init; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Tenant binding enforced uniformly across all management actions — see TenantScopeGuard.
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, TenantId, "assign-role") is { } tenantError)
            return Result<Guid, IError>.Failure(tenantError);

        // A role gives what it carries, so the assigner must hold it, as AssignPermissionsToRole asks of a grantor:
        // the assignment permission alone would otherwise give any role of the tenant, to anyone or to oneself.
        // Every grant not yet expired counts, a future one too. An operator with no tenant is the platform.
        if (_currentUser.TenantId is { Length: > 0 })
        {
            var grants = await _store.GetRolePermissionsAsync([RoleName], TenantId, ct).ConfigureAwait(false);
            foreach (var permission in grants.Where(g => g.ValidTo is null || g.ValidTo > now).Select(g => g.PermissionName).Distinct())
            {
                if (!await _currentUser.Authorization.HasPermissionAsync(permission, ct).ConfigureAwait(false))
                    return Result<Guid, IError>.Failure(ForbiddenError.ActionDenied("assign-role", $"permission:{permission}"));
            }
        }

        // Guard: prevent duplicate active assignment for the same user/role/tenant
        var assignments = await _store.GetUserRolesAsync(UserId, TenantId, ct).ConfigureAwait(false);
        if (assignments.Any(a => a.RoleName == RoleName && a.TenantId == TenantId && a.IsInForceAt(now)))
            return ConflictError.AlreadyExists("UserRoleAssignment", $"{UserId}/{RoleName}");

        var assignment = new UserRoleAssignment
        {
            UserId = UserId,
            RoleName = RoleName,
            TenantId = TenantId,
            AssignedBy = _currentUser.Id,
            ValidFrom = ValidFrom ?? now,
            ValidTo = ValidTo
        };

        await _store.AddUserRoleAsync(assignment, ct).ConfigureAwait(false);

        if (_permissionCache is not null)
            await _permissionCache.InvalidateUserAsync(UserId, ct).ConfigureAwait(false);

        return Result<Guid, IError>.Success(assignment.PersistenceId);
    }
}
