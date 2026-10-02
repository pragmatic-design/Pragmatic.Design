using Microsoft.EntityFrameworkCore;
using Pragmatic.Authorization.Management.Entities;
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
///         <c>DbContext</c> is the importing boundary's: the host bridges it for this package's services.
///     </para>
/// </remarks>
/// <param name="dbContext">The context of the boundary that imported the package.</param>
/// <param name="tenantContext">The resolved tenant, when multi-tenancy is in use.</param>
[Service<IPermissionProvider>(Multiple = true)]
public sealed class ManagedRolePermissionProvider(DbContext dbContext, ITenantContext? tenantContext = null)
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
        var userId = user.Id;

        var roles = await dbContext.Set<UserRoleAssignment>()
            .AsNoTracking()
            .Where(a => a.UserId == userId
                        && (a.TenantId == null || a.TenantId == tenant)
                        && a.ValidFrom <= now
                        && (a.ValidTo == null || a.ValidTo > now))
            .Select(a => a.RoleName)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (roles.Count == 0)
            return permissions;

        var granted = await dbContext.Set<DynamicRolePermission>()
            .AsNoTracking()
            .Where(rp => roles.Contains(rp.RoleName)
                         && (rp.TenantId == null || rp.TenantId == tenant)
                         && rp.ValidFrom <= now
                         && (rp.ValidTo == null || rp.ValidTo > now))
            .Select(rp => rp.PermissionName)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        permissions.UnionWith(granted);
        return permissions;
    }
}
