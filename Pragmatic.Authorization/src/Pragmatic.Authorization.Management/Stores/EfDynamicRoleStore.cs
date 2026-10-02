using Microsoft.EntityFrameworkCore;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Authorization.Stores;
using Pragmatic.Composition.Attributes;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Authorization.Management.Stores;

/// <summary>
///     EF Core implementation of <see cref="IDynamicRoleStore"/>.
///     Reads dynamic roles from the database, filtered by tenant context.
/// </summary>
/// <remarks>
///     <c>DbContext</c> is the importing boundary's: the host bridges it for this package's services.
/// </remarks>
[Service<IDynamicRoleStore>]
public sealed class EfDynamicRoleStore(
    DbContext dbContext,
    ICurrentUser currentUser,
    ITenantContext? tenantContext = null) : IDynamicRoleStore
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RoleInfo>> GetAllAsync(CancellationToken ct = default)
    {
        var tenantId = Tenant;

        var roles = await dbContext.Set<DynamicRole>()
            .Where(r => !r.IsDeleted && (r.TenantId == null || r.TenantId == tenantId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Resolve permissions for each role via DynamicRolePermission
        var now = DateTimeOffset.UtcNow;
        var roleNames = roles.Select(r => r.Name).ToList();
        var rolePermissions = await dbContext.Set<DynamicRolePermission>()
            .Where(rp => roleNames.Contains(rp.RoleName)
                         && rp.ValidFrom <= now
                         && (rp.ValidTo == null || rp.ValidTo > now)
                         && (rp.TenantId == null || rp.TenantId == tenantId))
            .GroupBy(rp => rp.RoleName)
            .ToDictionaryAsync(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(rp => rp.PermissionName).ToList(),
                ct)
            .ConfigureAwait(false);

        return roles.Select(r => new RoleInfo(
            r.Name,
            r.Description,
            rolePermissions.TryGetValue(r.Name, out var perms) ? perms : []
        )).ToList();
    }

    /// <inheritdoc />
    public async ValueTask<bool> ExistsAsync(string roleName, CancellationToken ct = default)
    {
        var tenantId = Tenant;

        return await dbContext.Set<DynamicRole>()
            .AnyAsync(r => !r.IsDeleted
                           && r.Name == roleName
                           && (r.TenantId == null || r.TenantId == tenantId), ct)
            .ConfigureAwait(false);
    }

    // The resolved tenant first and the claim second, as ManagedRolePermissionProvider reads them.
    private string? Tenant => tenantContext?.TenantId is { Length: > 0 } resolved ? resolved : currentUser.TenantId;
}
