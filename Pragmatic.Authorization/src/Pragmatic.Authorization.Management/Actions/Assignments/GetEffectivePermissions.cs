using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Authorization.Stores;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Assignments;

/// <summary>
///     Gets the effective permissions for a user by resolving all active role and group assignments
///     (group → role → permission, mirroring what GroupExpansionProvider grants at runtime).
/// </summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.View)]
public partial class GetEffectivePermissions : DomainAction<IReadOnlyList<string>>
{
    private IAuthorizationManagementStore _store = null!;
    private IRolePermissionStore _roleStore = null!;
    private ICurrentUser _currentUser = null!;

    // NOT named _serviceProvider: the Actions SG turns each injected field into an invoker ctor
    // parameter (camelCased), and the invoker already has its own `serviceProvider` parameter —
    // a field named _serviceProvider produces a duplicate parameter (CS0100). Resolved by type, so
    // the name is free. Used for the OPTIONAL IGroupRoleStore (the SG has no optional field injection).
    private IServiceProvider _rootServices = null!;

    public required string UserId { get; init; }
    public string? TenantId { get; init; }

    /// <summary>
    ///     The request's tenant, written by the invoker: over HTTP a body never carries <see cref="TenantId" />, so this is the scope for a
    ///     caller bound to a tenant (#778).
    /// </summary>
    [FromTenant]
    public string? RequestTenantId { get; private set; }

    public override async Task<Result<IReadOnlyList<string>, IError>> Execute(CancellationToken ct = default)
    {
        var tenantId = TenantScopeGuard.Scope(_currentUser, RequestTenantId, TenantId);
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, tenantId, "get-effective-permissions") is { } tenantError)
            return Result<IReadOnlyList<string>, IError>.Failure(tenantError);

        var now = DateTimeOffset.UtcNow;

        // The roles the user holds through this package: assigned, and given to a group they are a member of (#726), as the providers read them.
        var roleNames = await HeldRoles.OfAsync(_store, UserId, tenantId, now, ct).ConfigureAwait(false);

        // A group's roles the host declares (IGroupRoleStore, e.g. MapGroup) are added for the same memberships.
        // IGroupRoleStore is optional (absent when no group store is configured), so it is
        // resolved lazily instead of being a hard ctor dependency of the invoker.
        if (_rootServices.GetService<IGroupRoleStore>() is { } groupStore)
        {
            var activeGroups = (await _store.GetUserGroupsAsync(UserId, tenantId, ct).ConfigureAwait(false))
                .Where(a => a.IsInForceAt(now))
                .Select(a => a.GroupName)
                .Distinct();

            foreach (var group in activeGroups)
            {
                var groupRoles = await groupStore.GetRolesForGroupAsync(group, ct).ConfigureAwait(false);
                roleNames.UnionWith(groupRoles);
            }
        }

        // A deactivated role grants nothing (#725), the answer here included.
        roleNames.ExceptWith(RoleActivity.Deactivated(await _store.GetRolesAsync(tenantId, ct).ConfigureAwait(false), tenantId));

        // One store call per role, run in parallel: IRolePermissionStore reads a role at a time, so the
        // cost grows with the number of roles a user holds, not with a single round trip.
        var permissionSets = await Task.WhenAll(
            roleNames.Select(r => _roleStore.GetPermissionsForRoleAsync(r, ct).AsTask()))
            .ConfigureAwait(false);

        var allPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var permSet in permissionSets)
            foreach (var permission in permSet)
                allPermissions.Add(permission);

        // What this package's roles grant: the rows AssignPermissionsToRole writes, which ManagedRolePermissionProvider grants at runtime.
        // Read from the role store above alone, the answer left out every permission granted here (#725).
        if (roleNames.Count > 0)
            allPermissions.UnionWith((await _store.GetRolePermissionsAsync(roleNames, tenantId, ct).ConfigureAwait(false))
                .Where(rp => rp.IsInForceAt(now))
                .Select(rp => rp.PermissionName));

        IReadOnlyList<string> result = allPermissions.Order().ToList();
        return Result<IReadOnlyList<string>, IError>.Success(result);
    }
}
