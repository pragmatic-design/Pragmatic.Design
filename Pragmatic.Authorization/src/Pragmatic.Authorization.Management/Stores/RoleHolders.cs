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
            .Where(a => (tenantId == null || a.TenantId == null || a.TenantId == tenantId)
                        && (a.ValidTo == null || a.ValidTo > now))
            .Select(a => a.UserId)
            .Distinct();

        foreach (var holder in holders)
            await cache.InvalidateUserAsync(holder, ct).ConfigureAwait(false);
    }
}
