namespace Pragmatic.Authorization.Stores;

/// <summary>
///     Extends <see cref="IRolePermissionStore"/> with tenant-specific permission resolution.
///     Forward-compatible interface for L3 multi-tenant authorization.
/// </summary>
public interface ITenantRolePermissionStore : IRolePermissionStore
{
    /// <summary>Gets permissions for a role within a specific tenant context.</summary>
    ValueTask<IReadOnlySet<string>> GetPermissionsForRoleAsync(
        string roleName, string tenantId, CancellationToken ct = default);
}
