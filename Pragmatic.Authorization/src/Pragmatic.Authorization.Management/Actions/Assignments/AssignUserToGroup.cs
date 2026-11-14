using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Assignments;

/// <summary>
///     Makes a user a member of a group with optional temporal validity (#726): the user holds the group's roles, and what they grant, on the
///     next request.
/// </summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.Assignments.Manage)]
public partial class AssignUserToGroup : DomainAction<Guid>
{
    private IAuthorizationManagementStore _store = null!;
    private ICurrentUser _currentUser = null!;

    // Optional: registered only with a cross-request permission cache, and without one there is
    // nothing cached to evict — the next request resolves from the tables.
    private IPermissionCacheInvalidator? _permissionCache;

    public required string UserId { get; init; }
    public required string GroupName { get; init; }
    public string? TenantId { get; init; }
    public DateTimeOffset? ValidFrom { get; init; }
    public DateTimeOffset? ValidTo { get; init; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        if (TenantScopeGuard.CheckTenantBinding(_currentUser, TenantId, "assign-group") is { } tenantError)
            return Result<Guid, IError>.Failure(tenantError);

        // A membership gives the group's roles, so the caller must hold what they carry, as AssignRoleToUser asks of one role.
        // Every role of the group not yet ended counts, a future one too.
        var roles = (await _store.GetGroupRolesAsync([GroupName], TenantId, ct).ConfigureAwait(false))
            .Where(gr => gr.ValidTo is null || gr.ValidTo > now)
            .Select(gr => gr.RoleName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (await GrantorHolds.MissingAsync(_store, _currentUser, roles, TenantId, now, ct).ConfigureAwait(false) is { } missing)
            return Result<Guid, IError>.Failure(ForbiddenError.ActionDenied("assign-group", $"permission:{missing}"));

        var memberships = await _store.GetUserGroupsAsync(UserId, TenantId, ct).ConfigureAwait(false);
        if (memberships.Any(a => a.GroupName == GroupName && a.TenantId == TenantId && a.IsInForceAt(now)))
            return ConflictError.AlreadyExists("UserGroupAssignment", $"{UserId}/{GroupName}");

        var assignment = new UserGroupAssignment
        {
            UserId = UserId,
            GroupName = GroupName,
            TenantId = TenantId,
            AssignedBy = _currentUser.Id,
            ValidFrom = ValidFrom ?? now,
            ValidTo = ValidTo
        };

        await _store.AddUserGroupAsync(assignment, ct).ConfigureAwait(false);

        if (_permissionCache is not null)
            await _permissionCache.InvalidateUserAsync(UserId, ct).ConfigureAwait(false);

        return Result<Guid, IError>.Success(assignment.PersistenceId);
    }
}
