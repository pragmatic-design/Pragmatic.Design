namespace Pragmatic.Authorization.Stores;

/// <summary>
///     Store for dynamically managed roles (e.g., created at runtime via RBAC management).
///     Implementations: EfDynamicRoleStore (Phase 3).
/// </summary>
public interface IDynamicRoleStore
{
    /// <summary>Gets all dynamically defined roles.</summary>
    ValueTask<IReadOnlyList<RoleInfo>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Checks whether a dynamic role with the given name exists.</summary>
    ValueTask<bool> ExistsAsync(string roleName, CancellationToken ct = default);
}
