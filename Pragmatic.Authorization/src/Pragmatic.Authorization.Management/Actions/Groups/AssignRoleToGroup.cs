using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Groups;

/// <summary>
///     Gives a role to a group with optional temporal validity (#726): every member of the group holds the role, and what it grants, on the
///     next request.
/// </summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.Roles.Manage)]
public partial class AssignRoleToGroup : DomainAction<Guid>
{
    private IAuthorizationManagementStore _store = null!;
    private ICurrentUser _currentUser = null!;

    // Optional: registered only with a cross-request permission cache, and without one there is
    // nothing cached to evict — the next request resolves from the tables.
    private IPermissionCacheInvalidator? _permissionCache;

    public required string GroupName { get; init; }
    public required string RoleName { get; init; }
    public string? TenantId { get; init; }

    /// <summary>
    ///     The request's tenant, written by the invoker: over HTTP a body never carries <see cref="TenantId" />, so this is the scope for a
    ///     caller bound to a tenant (#778).
    /// </summary>
    [FromTenant]
    public string? RequestTenantId { get; private set; }
    public DateTimeOffset? ValidFrom { get; init; }
    public DateTimeOffset? ValidTo { get; init; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var tenantId = TenantScopeGuard.Scope(_currentUser, RequestTenantId, TenantId);
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, tenantId, "assign-group-role") is { } tenantError)
            return Result<Guid, IError>.Failure(tenantError);

        // The group's members get what the role carries, so the caller must hold it, as AssignRoleToUser asks.
        if (await GrantorHolds.MissingAsync(_store, _currentUser, [RoleName], tenantId, now, ct).ConfigureAwait(false) is { } missing)
            return Result<Guid, IError>.Failure(ForbiddenError.ActionDenied("assign-group-role", $"permission:{missing}"));

        var given = await _store.GetGroupRolesAsync([GroupName], tenantId, ct).ConfigureAwait(false);
        if (given.Any(gr => gr.GroupName == GroupName && gr.RoleName == RoleName && gr.TenantId == tenantId && gr.IsInForceAt(now)))
            return ConflictError.AlreadyExists("GroupRoleAssignment", $"{GroupName}/{RoleName}");

        var assignment = new GroupRoleAssignment
        {
            GroupName = GroupName,
            RoleName = RoleName,
            TenantId = tenantId,
            AssignedBy = _currentUser.Id,
            ValidFrom = ValidFrom ?? now,
            ValidTo = ValidTo
        };

        await _store.AddGroupRoleAsync(assignment, ct).ConfigureAwait(false);
        await GroupMembers.InvalidateAsync(_store, _permissionCache, GroupName, tenantId, now, ct).ConfigureAwait(false);

        return Result<Guid, IError>.Success(assignment.PersistenceId);
    }
}
