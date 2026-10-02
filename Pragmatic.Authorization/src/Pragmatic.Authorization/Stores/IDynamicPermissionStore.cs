namespace Pragmatic.Authorization.Stores;

/// <summary>
///     Store for dynamically managed permissions (e.g., created at runtime via RBAC management).
///     Implementations: EfDynamicPermissionStore (Phase 3).
/// </summary>
public interface IDynamicPermissionStore
{
    /// <summary>Gets all dynamically defined permissions.</summary>
    ValueTask<IReadOnlyList<PermissionInfo>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Checks whether a dynamic permission with the given name exists.</summary>
    ValueTask<bool> ExistsAsync(string permissionName, CancellationToken ct = default);
}
