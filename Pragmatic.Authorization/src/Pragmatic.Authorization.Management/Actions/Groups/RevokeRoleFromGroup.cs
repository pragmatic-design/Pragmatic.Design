using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Groups;

/// <summary>
///     Takes a role back from a group by ending it now (#726): the group's members stop holding it on the next request, and the row stays as
///     the history of what the group held.
/// </summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.Roles.Manage)]
public partial class RevokeRoleFromGroup : VoidDomainAction<NotFoundError>
{
    private IAuthorizationManagementStore _store = null!;
    private ICurrentUser _currentUser = null!;

    // Optional: registered only with a cross-request permission cache, and without one there is
    // nothing cached to evict — the next request resolves from the tables.
    private IPermissionCacheInvalidator? _permissionCache;

    public required string GroupName { get; init; }
    public required string RoleName { get; init; }
    public string? TenantId { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, TenantId, "revoke-group-role") is { } tenantError)
            return VoidResult<IError>.Failure(tenantError);

        var now = DateTimeOffset.UtcNow;

        var given = await _store.GetGroupRolesAsync([GroupName], TenantId, ct).ConfigureAwait(false);
        var assignment = given.FirstOrDefault(gr => gr.GroupName == GroupName && gr.RoleName == RoleName && gr.TenantId == TenantId && gr.IsInForceAt(now));

        if (assignment is null)
            return VoidResult<IError>.Failure(NotFoundError.Create("GroupRoleAssignment", $"{GroupName}/{RoleName}"));

        await _store.EndGroupRoleAsync(assignment, now, ct).ConfigureAwait(false);

        // A revocation that waits for the cache to expire is a revocation that has not happened yet.
        await GroupMembers.InvalidateAsync(_store, _permissionCache, GroupName, TenantId, now, ct).ConfigureAwait(false);

        return VoidResult<IError>.Success();
    }
}
