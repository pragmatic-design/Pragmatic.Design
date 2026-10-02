using Microsoft.EntityFrameworkCore;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Authorization.Stores;
using Pragmatic.Composition.Attributes;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Authorization.Management.Stores;

/// <summary>
///     EF Core implementation of <see cref="IDynamicPermissionStore"/>.
///     Reads dynamic permissions from the database, filtered by tenant context.
/// </summary>
/// <remarks>
///     <c>DbContext</c> is the importing boundary's: the host bridges it for this package's services.
/// </remarks>
[Service<IDynamicPermissionStore>]
public sealed class EfDynamicPermissionStore(
    DbContext dbContext,
    ICurrentUser currentUser,
    ITenantContext? tenantContext = null) : IDynamicPermissionStore
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PermissionInfo>> GetAllAsync(CancellationToken ct = default)
    {
        var tenantId = Tenant;

        return await dbContext.Set<DynamicPermission>()
            .Where(p => !p.IsDeleted && (p.TenantId == null || p.TenantId == tenantId))
            .Select(p => new PermissionInfo(p.Name, p.Description, p.Category))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> ExistsAsync(string permissionName, CancellationToken ct = default)
    {
        var tenantId = Tenant;

        return await dbContext.Set<DynamicPermission>()
            .AnyAsync(p => !p.IsDeleted && p.Name == permissionName
                           && (p.TenantId == null || p.TenantId == tenantId), ct)
            .ConfigureAwait(false);
    }

    // The resolved tenant first and the claim second, as ManagedRolePermissionProvider reads them.
    private string? Tenant => tenantContext?.TenantId is { Length: > 0 } resolved ? resolved : currentUser.TenantId;
}
