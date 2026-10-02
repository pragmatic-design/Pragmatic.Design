namespace Pragmatic.Authorization.Catalog;

/// <summary>
///     Unified catalog of permissions and roles, merging static (SG-generated)
///     and dynamic (database-backed) sources.
/// </summary>
public interface IPermissionCatalog
{
    /// <summary>
    ///     Gets all known permissions (static + dynamic).
    /// </summary>
    ValueTask<IReadOnlyList<PermissionInfo>> GetAllPermissionsAsync(CancellationToken ct = default);

    /// <summary>
    ///     Gets all known roles (static + dynamic).
    /// </summary>
    ValueTask<IReadOnlyList<RoleInfo>> GetAllRolesAsync(CancellationToken ct = default);

    /// <summary>
    ///     Gets the effective permissions for a user by resolving the full
    ///     permission chain (claims → roles → groups → dynamic).
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask<IReadOnlySet<string>> GetEffectivePermissionsAsync(string userId, CancellationToken ct = default);
}
