using Pragmatic.Authorization.Stores;

namespace Pragmatic.Authorization.Catalog;

/// <summary>
///     Default catalog that merges SG-generated static permissions/roles
///     with optional dynamic stores (database-backed).
/// </summary>
/// <remarks>
///     <para>
///         Static data is provided via DI as <see cref="IEnumerable{PermissionInfo}"/> and
///         <see cref="IEnumerable{RoleInfo}"/>. Each SG-generated <c>AddGeneratedAuthorizationCatalog()</c>
///         (emitted per assembly that declares permissions or <c>IRole</c> types) registers every
///         entry of its <c>PermissionRegistry.All</c>/<c>RoleRegistry.All</c> as an individual singleton,
///         so the constructor injection here aggregates the permissions and roles across all assemblies.
///     </para>
///     <para>
///         Dynamic stores are optional — when not registered, only static data is returned.
///     </para>
/// </remarks>
public sealed class DefaultPermissionCatalog(
    IEnumerable<PermissionInfo>? staticPermissions = null,
    IEnumerable<RoleInfo>? staticRoles = null,
    IDynamicPermissionStore? dynamicPermissionStore = null,
    IDynamicRoleStore? dynamicRoleStore = null) : IPermissionCatalog
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PermissionInfo>> GetAllPermissionsAsync(CancellationToken ct = default)
    {
        var result = new List<PermissionInfo>(staticPermissions ?? []);

        if (dynamicPermissionStore is not null)
        {
            var dynamic = await dynamicPermissionStore.GetAllAsync(ct).ConfigureAwait(false);
            result.AddRange(dynamic);
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RoleInfo>> GetAllRolesAsync(CancellationToken ct = default)
    {
        var result = new List<RoleInfo>(staticRoles ?? []);

        if (dynamicRoleStore is not null)
        {
            var dynamic = await dynamicRoleStore.GetAllAsync(ct).ConfigureAwait(false);
            result.AddRange(dynamic);
        }

        return result;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Not yet implemented — returns an empty set (safe: deny-by-default).
    ///     Full cross-user permission resolution requires RBAC Management stores.
    ///     For the current user's permissions, use <c>ICurrentUser.Authorization.Permissions</c>.
    /// </remarks>
    public ValueTask<IReadOnlySet<string>> GetEffectivePermissionsAsync(string userId, CancellationToken ct = default)
    {
        // Returns empty set (deny-by-default) rather than throwing.
        // Full implementation requires user→role→permission resolution via stores,
        // which is only available when RBAC Management package is wired.
        IReadOnlySet<string> empty = new HashSet<string>();
        return new ValueTask<IReadOnlySet<string>>(empty);
    }
}
