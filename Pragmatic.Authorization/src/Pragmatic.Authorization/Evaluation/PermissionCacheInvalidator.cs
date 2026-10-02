using Pragmatic.Authorization.Stores;
using Pragmatic.Caching;

namespace Pragmatic.Authorization.Evaluation;

/// <summary>
///     Default <see cref="IPermissionCacheInvalidator"/> — invalidates the permission
///     cache through the tags written by <see cref="CachedPermissionResolver"/>
///     (<c>user:{id}</c>, <c>tenant:{id}</c>, <c>role:{name}</c> and <c>group:{name}</c>).
/// </summary>
/// <remarks>
///     A cached permission set is tagged <c>role:{r}</c> only for the user's <b>direct</b> role
///     claims and <c>group:{g}</c> for group claims — never the roles a group expands into. So
///     invalidating a single <c>role:{r}</c> tag would miss a user who holds <c>r</c> transitively
///     through a group (their entry is tagged <c>group:{g}</c>, not <c>role:{r}</c>). To close that
///     gap <see cref="InvalidateRoleAsync"/> also reverse-looks-up every group that grants the role
///     (via <see cref="IGroupRoleStore"/>) and drops those <c>group:{g}</c> tags too. Invalidation is a
///     rare admin operation, so the extra store round-trips are paid there rather than on every request.
/// </remarks>
internal sealed class PermissionCacheInvalidator(
    ICacheStack cache,
    IGroupRoleStore? groupStore = null) : IPermissionCacheInvalidator
{
    /// <inheritdoc />
    public ValueTask InvalidateUserAsync(string userId, CancellationToken ct = default)
        => cache.InvalidateByTagAsync($"user:{userId}", ct);

    /// <inheritdoc />
    public ValueTask InvalidateTenantAsync(string tenantId, CancellationToken ct = default)
        => cache.InvalidateByTagAsync($"tenant:{tenantId}", ct);

    /// <inheritdoc />
    public ValueTask InvalidateRoleAsync(string roleName, CancellationToken ct = default)
        // Fast path: no group store configured → only direct role holders can be tagged with this role.
        => groupStore is null
            ? cache.InvalidateByTagAsync($"role:{roleName}", ct)
            : InvalidateRoleWithGroupsAsync(roleName, ct);

    /// <inheritdoc />
    public ValueTask InvalidateGroupAsync(string groupName, CancellationToken ct = default)
        => cache.InvalidateByTagAsync($"group:{groupName}", ct);

    // Invalidates the role tag AND the tags of every group that grants the role, so users who hold
    // the role transitively via a group (tagged group:{g}, not role:{r}) are evicted as well.
    private async ValueTask InvalidateRoleWithGroupsAsync(string roleName, CancellationToken ct)
    {
        // Reverse lookup: enumerate groups, keep those that grant the role. The interface exposes no
        // direct role → groups index, so this fans out over the (typically small) group set. Correct
        // for the multi-hop group → role → permission case; role → role nesting is not modeled.
        var tags = new List<string> { $"role:{roleName}" };

        var groups = await groupStore!.GetAllGroupsAsync(ct).ConfigureAwait(false);
        foreach (var group in groups)
        {
            var roles = await groupStore.GetRolesForGroupAsync(group, ct).ConfigureAwait(false);
            if (roles.Contains(roleName))
                tags.Add($"group:{group}");
        }

        await cache.InvalidateByTagsAsync(tags, ct).ConfigureAwait(false);
    }
}
