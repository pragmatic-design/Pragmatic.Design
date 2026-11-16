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
///     Takes permissions back from a role by ending their grants now (#725): the rows stay, as the history of what the role granted, and the
///     role's holders lose the permissions on their next request.
/// </summary>
/// <remarks>
///     The grants ended are those of exactly <see cref="TenantId" />, as <c>AssignPermissionsToRole</c> writes them: a global grant of the
///     same permission is another row, revoked with no tenant. A permission the role does not hold there is not found, so a revocation that
///     changed nothing says so.
/// </remarks>
[DomainAction]
[RequirePermission(AuthorizationPermissions.Roles.Manage)]
public partial class RevokePermissionsFromRole : VoidDomainAction<NotFoundError>
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
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, tenantId, "revoke-permissions-from-role") is { } tenantError)
            return VoidResult<IError>.Failure(tenantError);

        var now = DateTimeOffset.UtcNow;
        var revoked = new HashSet<string>(Permissions, StringComparer.OrdinalIgnoreCase);

        var grants = (await _store.GetRolePermissionsAsync([RoleName], tenantId, ct).ConfigureAwait(false))
            .Where(rp => rp.RoleName == RoleName && rp.TenantId == tenantId && rp.IsInForceAt(now) && revoked.Contains(rp.PermissionName))
            .ToList();

        if (grants.Count == 0)
            return VoidResult<IError>.Failure(NotFoundError.Create("DynamicRolePermission", $"{RoleName}/{string.Join(",", Permissions)}"));

        await _store.EndRolePermissionsAsync(grants, now, ct).ConfigureAwait(false);
        await RoleHolders.InvalidateAsync(_store, _permissionCache, RoleName, tenantId, now, ct).ConfigureAwait(false);

        return VoidResult<IError>.Success();
    }
}
