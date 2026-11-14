using Pragmatic.Authorization.Management.Entities;

namespace Pragmatic.Authorization.Management.Stores;

/// <summary>
///     Which role names grant nothing in a tenant because their role is deactivated (#725): the one rule the permission provider, the role
///     provider and <c>GetEffectivePermissions</c> apply, so the three agree on what a deactivated role takes away.
/// </summary>
/// <remarks>
///     A tenant's own role of a name governs that name in the tenant, and the global role of the name governs it elsewhere: the order
///     <c>CreateRole</c>'s uniqueness allows, one role of a name per scope. A name no role row carries (a grant to a role nobody created) is
///     active.
/// </remarks>
internal static class RoleActivity
{
    /// <summary>The names, among <paramref name="roles" /> (global and of <paramref name="tenantId" />), whose governing role is deactivated.</summary>
    public static HashSet<string> Deactivated(IReadOnlyList<DynamicRole> roles, string? tenantId)
    {
        var deactivated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var byName in roles.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            var governing = byName.FirstOrDefault(r => tenantId is not null && r.TenantId == tenantId)
                            ?? byName.FirstOrDefault(r => r.TenantId is null);
            if (governing?.DeactivatedAt is not null)
                deactivated.Add(byName.Key);
        }

        return deactivated;
    }

    /// <summary>The role named <paramref name="name" /> in exactly <paramref name="tenantId" />'s scope, or <see langword="null" />.</summary>
    public static DynamicRole? InScope(IReadOnlyList<DynamicRole> roles, string name, string? tenantId)
        => roles.FirstOrDefault(r => r.Name == name && r.TenantId == tenantId);
}
