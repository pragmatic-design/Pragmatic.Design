namespace Pragmatic.Authorization.Management.Stores;

/// <summary>
///     The roles a user holds through this package at an instant: the ones assigned to them (<c>AssignRoleToUser</c>), and the ones given to a
///     group they are a member of (<c>AssignRoleToGroup</c>, <c>AssignUserToGroup</c>, #726).
/// </summary>
/// <remarks>
///     The one answer the permission provider, the role provider and <c>GetEffectivePermissions</c> read, so a group's role is granted, listed
///     among the caller's roles and shown to the administrator alike. A group's role holds while both the membership and the role of the group
///     are in force, each in the tenant asked or global.
/// </remarks>
internal static class HeldRoles
{
    public static async Task<HashSet<string>> OfAsync(
        IAuthorizationManagementStore store, string userId, string? tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var roles = new HashSet<string>(
            (await store.GetUserRolesAsync(userId, tenantId, ct).ConfigureAwait(false)).Where(a => a.IsInForceAt(now)).Select(a => a.RoleName),
            StringComparer.OrdinalIgnoreCase);

        var groups = (await store.GetUserGroupsAsync(userId, tenantId, ct).ConfigureAwait(false))
            .Where(a => a.IsInForceAt(now))
            .Select(a => a.GroupName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (groups.Count > 0)
            roles.UnionWith((await store.GetGroupRolesAsync(groups, tenantId, ct).ConfigureAwait(false))
                .Where(gr => gr.IsInForceAt(now))
                .Select(gr => gr.RoleName));

        return roles;
    }
}
