using Pragmatic.Authorization.Stores;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Providers;

/// <summary>
///     Expands user groups into permissions via Group → Roles → Permissions chain.
///     Runs after role expansion (Order = 200).
/// </summary>
public sealed class GroupExpansionProvider(
    IGroupRoleStore groupStore,
    IRolePermissionStore roleStore) : IPermissionProvider
{
    /// <inheritdoc />
    public int Order => 200;

    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(
        ICurrentUser user, CancellationToken ct = default)
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!user.Claims.TryGetValue("group", out var groups))
            return merged;

        // One store call per group, run in parallel: IGroupRoleStore reads a group at a time, so the cost
        // grows with the number of groups in the claim, not with a single round trip.
        var roleSets = await Task.WhenAll(
            groups.Select(g => groupStore.GetRolesForGroupAsync(g, ct).AsTask()))
            .ConfigureAwait(false);

        var allRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var roleSet in roleSets)
            foreach (var role in roleSet)
                allRoles.Add(role);

        // Likewise one call per role, in parallel.
        var permissionSets = await Task.WhenAll(
            allRoles.Select(r => roleStore.GetPermissionsForRoleAsync(r, ct).AsTask()))
            .ConfigureAwait(false);

        foreach (var permSet in permissionSets)
            foreach (var p in permSet)
                merged.Add(p);

        return merged;
    }
}
