using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Assignments;

/// <summary>
///     Ends a user's membership of a group now (#726): the user stops holding the group's roles on the next request, and the row stays as the
///     history of who was a member.
/// </summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.Assignments.Manage)]
public partial class RemoveUserFromGroup : VoidDomainAction<NotFoundError>
{
    private IAuthorizationManagementStore _store = null!;
    private ICurrentUser _currentUser = null!;

    // Optional: registered only with a cross-request permission cache, and without one there is
    // nothing cached to evict — the next request resolves from the tables.
    private IPermissionCacheInvalidator? _permissionCache;

    public required string UserId { get; init; }
    public required string GroupName { get; init; }
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
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, tenantId, "remove-group") is { } tenantError)
            return VoidResult<IError>.Failure(tenantError);

        var now = DateTimeOffset.UtcNow;

        var memberships = await _store.GetUserGroupsAsync(UserId, tenantId, ct).ConfigureAwait(false);
        var membership = memberships.FirstOrDefault(a => a.GroupName == GroupName && a.TenantId == tenantId && a.IsInForceAt(now));

        if (membership is null)
            return VoidResult<IError>.Failure(NotFoundError.Create("UserGroupAssignment", $"{UserId}/{GroupName}"));

        await _store.EndUserGroupAsync(membership, now, ct).ConfigureAwait(false);

        // A removal that waits for the cache to expire is a removal that has not happened yet.
        if (_permissionCache is not null)
            await _permissionCache.InvalidateUserAsync(UserId, ct).ConfigureAwait(false);

        return VoidResult<IError>.Success();
    }
}
