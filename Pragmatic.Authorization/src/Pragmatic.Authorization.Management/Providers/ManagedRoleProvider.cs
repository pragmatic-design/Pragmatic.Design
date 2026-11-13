using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Composition.Attributes;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Authorization.Management.Providers;

/// <summary>
///     The roles assigned to the caller through this package, by name: what <c>AssignRoleToUser</c> wrote and is in force now, in the
///     request's tenant or global.
/// </summary>
/// <remarks>
///     <see cref="ManagedRolePermissionProvider" /> grants what these roles grant; this puts their names in the caller's roles, so a role
///     check agrees with the permissions (#723). The same rows and the same rule for the tenant: the resolved one first, the claim second.
/// </remarks>
/// <param name="store">The rows of the boundary that imported the package.</param>
/// <param name="tenantContext">The resolved tenant, when multi-tenancy is in use.</param>
[Service<IRoleProvider>(Multiple = true)]
public sealed class ManagedRoleProvider(IAuthorizationManagementStore store, ITenantContext? tenantContext = null) : IRoleProvider
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyCollection<string>> ResolveRolesAsync(ICurrentUser user, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated)
            return [];

        var tenant = tenantContext?.TenantId is { Length: > 0 } resolved ? resolved : user.TenantId;
        var now = DateTimeOffset.UtcNow;

        return [.. (await store.GetUserRolesAsync(user.Id, tenant, ct).ConfigureAwait(false))
            .Where(a => a.IsInForceAt(now))
            .Select(a => a.RoleName)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
