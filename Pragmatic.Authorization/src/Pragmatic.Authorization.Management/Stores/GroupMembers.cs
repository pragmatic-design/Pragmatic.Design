using Pragmatic.Authorization.Evaluation;

namespace Pragmatic.Authorization.Management.Stores;

/// <summary>
///     Evicts the cached permissions of a group's members, after a change to the roles the group holds (#726).
/// </summary>
/// <remarks>
///     As <see cref="RoleHolders" /> for a role's holders: the cache tags a user's claim groups, not the groups this package assigns, so the
///     members are found and evicted one by one. A membership counts in the tenant changed and globally, and while it has not ended.
/// </remarks>
internal static class GroupMembers
{
    public static async Task InvalidateAsync(
        IAuthorizationManagementStore store, IPermissionCacheInvalidator? cache, string groupName, string? tenantId, DateTimeOffset now,
        CancellationToken ct)
    {
        if (cache is null)
            return;

        foreach (var member in await UserIdsAsync(store, groupName, tenantId, now, ct).ConfigureAwait(false))
            await cache.InvalidateUserAsync(member, ct).ConfigureAwait(false);
    }

    /// <summary>The members of <paramref name="groupName" /> whose permissions a change in <paramref name="tenantId" /> reaches.</summary>
    public static async Task<IReadOnlyList<string>> UserIdsAsync(
        IAuthorizationManagementStore store, string groupName, string? tenantId, DateTimeOffset now, CancellationToken ct)
        => [.. (await store.GetGroupMembersAsync(groupName, ct).ConfigureAwait(false))
            .Where(a => (tenantId == null || a.TenantId == null || a.TenantId == tenantId) && (a.ValidTo == null || a.ValidTo > now))
            .Select(a => a.UserId)
            .Distinct()];
}
