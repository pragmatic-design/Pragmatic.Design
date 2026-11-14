using Pragmatic.Authorization.Evaluation;

namespace Pragmatic.Authorization.Management.Stores;

/// <summary>
///     Evicts the cached permissions of the users who hold a role, after a change to what the role grants.
/// </summary>
/// <remarks>
///     The cache tags a user's claim roles, not the roles this package assigns, so the users who hold the role in the tenant are found and
///     evicted one by one. Shared by the actions that change a role's grants, so each change reaches the same holders.
/// </remarks>
internal static class RoleHolders
{
    public static async Task InvalidateAsync(
        IAuthorizationManagementStore store, IPermissionCacheInvalidator? cache, string roleName, string? tenantId, DateTimeOffset now,
        CancellationToken ct)
    {
        if (cache is null)
            return;

        var holders = (await store.GetRoleHoldersAsync(roleName, ct).ConfigureAwait(false))
            .Where(a => InScope(a.TenantId, a.ValidTo, tenantId, now))
            .Select(a => a.UserId)
            .ToHashSet();

        // The members of the groups the role was given to hold it too (#726).
        var groups = (await store.GetRoleGroupsAsync(roleName, ct).ConfigureAwait(false))
            .Where(gr => InScope(gr.TenantId, gr.ValidTo, tenantId, now))
            .Select(gr => gr.GroupName)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
            holders.UnionWith(await GroupMembers.UserIdsAsync(store, group, tenantId, now, ct).ConfigureAwait(false));

        foreach (var holder in holders)
            await cache.InvalidateUserAsync(holder, ct).ConfigureAwait(false);
    }

    // A row reaches a change in tenantId when it is of that tenant or global, or the change is global, and it has not ended.
    private static bool InScope(string? rowTenant, DateTimeOffset? validTo, string? tenantId, DateTimeOffset now)
        => (tenantId == null || rowTenant == null || rowTenant == tenantId) && (validTo == null || validTo > now);
}
