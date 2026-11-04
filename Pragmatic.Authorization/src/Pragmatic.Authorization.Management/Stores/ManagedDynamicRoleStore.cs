using Pragmatic.Authorization.Stores;
using Pragmatic.Composition.Attributes;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Authorization.Management.Stores;

/// <summary>
///     The <see cref="IDynamicRoleStore"/> of the roles this package manages, read through the importing boundary's
///     <see cref="IAuthorizationManagementStore"/>, filtered by tenant context.
/// </summary>
/// <remarks>
///     The store is the importing boundary's, on its engine: the host bridges it for this package's services.
/// </remarks>
[Service<IDynamicRoleStore>]
public sealed class ManagedDynamicRoleStore(
    IAuthorizationManagementStore store,
    ICurrentUser currentUser,
    ITenantContext? tenantContext = null) : IDynamicRoleStore
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RoleInfo>> GetAllAsync(CancellationToken ct = default)
    {
        var tenantId = Tenant;
        var roles = await store.GetRolesAsync(tenantId, ct).ConfigureAwait(false);

        // Resolve permissions for each role via DynamicRolePermission
        var now = DateTimeOffset.UtcNow;
        var roleNames = roles.Select(r => r.Name).ToList();
        var rolePermissions = (await store.GetRolePermissionsAsync(roleNames, tenantId, ct).ConfigureAwait(false))
            .Where(rp => rp.IsInForceAt(now))
            .GroupBy(rp => rp.RoleName)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(rp => rp.PermissionName).ToList());

        return roles.Select(r => new RoleInfo(
            r.Name,
            r.Description,
            rolePermissions.TryGetValue(r.Name, out var perms) ? perms : []
        )).ToList();
    }

    /// <inheritdoc />
    public async ValueTask<bool> ExistsAsync(string roleName, CancellationToken ct = default)
        => (await store.GetRolesAsync(Tenant, ct).ConfigureAwait(false)).Any(r => r.Name == roleName);

    // The resolved tenant first and the claim second, as ManagedRolePermissionProvider reads them.
    private string? Tenant => tenantContext?.TenantId is { Length: > 0 } resolved ? resolved : currentUser.TenantId;
}
