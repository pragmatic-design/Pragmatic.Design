namespace Pragmatic.Authorization.Stores;

/// <summary>
///     Resolves permissions assigned to a role.
///     Pluggable: in-memory for dev/test, database-backed for production.
/// </summary>
public interface IRolePermissionStore
{
    /// <summary>Gets all permissions assigned to the specified role.</summary>
    ValueTask<IReadOnlySet<string>> GetPermissionsForRoleAsync(
        string roleName, CancellationToken ct = default);

    /// <summary>Gets all known roles.</summary>
    ValueTask<IReadOnlyList<string>> GetAllRolesAsync(CancellationToken ct = default);
}
