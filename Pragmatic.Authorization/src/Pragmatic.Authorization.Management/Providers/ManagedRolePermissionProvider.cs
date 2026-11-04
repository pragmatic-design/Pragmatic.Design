using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Composition.Attributes;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Authorization.Management.Providers;

/// <summary>
///     The permissions of the roles assigned to the caller through this package: the rows
///     <c>AssignRoleToUser</c> and <c>AssignPermissionsToRole</c> write, read on every permission
///     resolution.
/// </summary>
/// <remarks>
///     <para>
///         Without it the package recorded assignments that nothing enforced: a user's roles came from their
///         identity alone, so assigning a role granted nothing and revoking one took nothing away, while
///         the administrator was told it had succeeded.
///     </para>
///     <para>
///         Only what is valid now, and only in the tenant the request resolved — or global. The tenant is
///         the resolved one first and the claim second, the same order the permission cache keys on: an
///         application that resolves tenants by header without a tenant claim would otherwise read only
///         the global rows.
///     </para>
///     <para>
///         The store is the importing boundary's, on its engine: the host bridges it for this package's services.
///     </para>
/// </remarks>
/// <param name="store">The rows of the boundary that imported the package.</param>
/// <param name="tenantContext">The resolved tenant, when multi-tenancy is in use.</param>
[Service<IPermissionProvider>(Multiple = true)]
public sealed class ManagedRolePermissionProvider(IAuthorizationManagementStore store, ITenantContext? tenantContext = null)
    : IPermissionProvider
{
    /// <inheritdoc />
    /// <remarks>After the roles the identity carries (100), before groups (200).</remarks>
    public int Order => 150;

    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(
        ICurrentUser user, CancellationToken ct = default)
    {
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!user.IsAuthenticated)
            return permissions;

        var tenant = tenantContext?.TenantId is { Length: > 0 } resolved ? resolved : user.TenantId;
        var now = DateTimeOffset.UtcNow;

        var roles = (await store.GetUserRolesAsync(user.Id, tenant, ct).ConfigureAwait(false))
            .Where(a => a.IsInForceAt(now))
            .Select(a => a.RoleName)
            .Distinct()
            .ToList();

        if (roles.Count == 0)
            return permissions;

        var granted = (await store.GetRolePermissionsAsync(roles, tenant, ct).ConfigureAwait(false))
            .Where(rp => rp.IsInForceAt(now))
            .Select(rp => rp.PermissionName);

        permissions.UnionWith(granted);
        return permissions;
    }
}
