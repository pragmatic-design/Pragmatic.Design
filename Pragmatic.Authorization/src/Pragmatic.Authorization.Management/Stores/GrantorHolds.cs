using Pragmatic.Identity;

namespace Pragmatic.Authorization.Management.Stores;

/// <summary>
///     Whether the caller holds what the roles they are giving carry: the rule <c>AssignRoleToUser</c>, <c>AssignRoleToGroup</c> and
///     <c>AssignUserToGroup</c> apply, so giving a role through a group is bound as giving it to a user is (#726).
/// </summary>
/// <remarks>
///     Without it the assignment permission alone would give any role of the tenant, to anyone or to oneself, directly or through a group.
///     Every grant not yet expired counts, a future one too. A caller with no tenant is the platform, and is not bound.
/// </remarks>
internal static class GrantorHolds
{
    /// <summary>The first permission the roles carry that the caller does not hold, or <see langword="null" /> when they hold them all.</summary>
    public static async Task<string?> MissingAsync(
        IAuthorizationManagementStore store, ICurrentUser caller, IReadOnlyCollection<string> roleNames, string? tenantId, DateTimeOffset now,
        CancellationToken ct)
    {
        if (caller.TenantId is not { Length: > 0 } || roleNames.Count == 0)
            return null;

        var grants = await store.GetRolePermissionsAsync(roleNames, tenantId, ct).ConfigureAwait(false);
        foreach (var permission in grants.Where(g => g.ValidTo is null || g.ValidTo > now).Select(g => g.PermissionName).Distinct())
        {
            if (!await caller.Authorization.HasPermissionAsync(permission, ct).ConfigureAwait(false))
                return permission;
        }

        return null;
    }
}
