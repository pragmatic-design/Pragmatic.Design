using Pragmatic.Authorization.Stores;
using Pragmatic.Composition.Attributes;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Authorization.Management.Stores;

/// <summary>
///     The <see cref="IDynamicPermissionStore"/> of the permissions this package manages, read through the importing boundary's
///     <see cref="IAuthorizationManagementStore"/>, filtered by tenant context.
/// </summary>
/// <remarks>
///     The store is the importing boundary's, on its engine: the host bridges it for this package's services.
/// </remarks>
[Service<IDynamicPermissionStore>]
public sealed class ManagedDynamicPermissionStore(
    IAuthorizationManagementStore store,
    ICurrentUser currentUser,
    ITenantContext? tenantContext = null) : IDynamicPermissionStore
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PermissionInfo>> GetAllAsync(CancellationToken ct = default)
        => (await store.GetPermissionsAsync(Tenant, ct).ConfigureAwait(false))
            .Select(p => new PermissionInfo(p.Name, p.Description, p.Category))
            .ToList();

    /// <inheritdoc />
    public async ValueTask<bool> ExistsAsync(string permissionName, CancellationToken ct = default)
        => (await store.GetPermissionsAsync(Tenant, ct).ConfigureAwait(false)).Any(p => p.Name == permissionName);

    // The resolved tenant first and the claim second, as ManagedRolePermissionProvider reads them.
    private string? Tenant => tenantContext?.TenantId is { Length: > 0 } resolved ? resolved : currentUser.TenantId;
}
