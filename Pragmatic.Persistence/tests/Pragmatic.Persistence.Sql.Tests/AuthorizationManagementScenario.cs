using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Management.Actions.Assignments;
using Pragmatic.Authorization.Management.Actions.Permissions;
using Pragmatic.Authorization.Management.Actions.Roles;
using Pragmatic.Authorization.Management.Providers;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Result.Http;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     Pragmatic.Authorization.Management's operations through their generated invokers, over the store of one engine: the same
///     scenario, with the same expectations, on the Sql engine's five databases and on the documents engine (linked into that suite).
/// </summary>
/// <remarks>
///     The expectations are the package's, written here by hand: a name taken twice is a conflict in its tenant and not across tenants,
///     permissions are added to a role and never replace its grants, an assigned role grants on the next resolution, a revoked one stops,
///     and the package's role and permission stores list what was created in the caller's tenant and globally.
/// </remarks>
internal static class AuthorizationManagementScenario
{
    public static async Task RunAsync(IAuthorizationManagementStore store)
    {
        var admin = new TestUser("admin"); // no tenant: the platform, which may write global rows
        var services = new ServiceCollection().BuildServiceProvider();
        var suffix = Guid.NewGuid().ToString("N")[..8]; // the databases are shared by the suite's tests
        var role = "auditor-" + suffix;
        var permission = "reports.export." + suffix;
        var user = "ada-" + suffix;

        // Create a permission and a role; the same name again in the same tenant is a conflict, in another tenant it is not.
        var createPermission = new CreatePermission.Invoker(store, admin, services);
        Assert.True((await createPermission.InvokeAsync(new CreatePermission { Name = permission, Category = "reports" })).IsSuccess);
        Assert.IsType<ConflictError>((await createPermission.InvokeAsync(new CreatePermission { Name = permission })).Error);

        var createRole = new CreateRole.Invoker(store, admin, services);
        Assert.True((await createRole.InvokeAsync(new CreateRole { Name = role, Description = "Reads the reports" })).IsSuccess);
        Assert.IsType<ConflictError>((await createRole.InvokeAsync(new CreateRole { Name = role })).Error);
        Assert.True((await createRole.InvokeAsync(new CreateRole { Name = role, TenantId = "acme-" + suffix })).IsSuccess);

        // Assign permissions to the role; nobody holds it yet, so nobody's cached permissions are evicted.
        var cache = new RecordingCache();
        var assignPermissions = new AssignPermissionsToRole.Invoker(store, admin, cache, services);
        Assert.True((await assignPermissions.InvokeAsync(new AssignPermissionsToRole { RoleName = role, Permissions = [permission, "reports.read"] })).IsSuccess);
        Assert.Empty(cache.Evicted);

        // Assign the role, which evicts its holder; the same assignment again is a conflict, and evicts nothing.
        var assignRole = new AssignRoleToUser.Invoker(store, admin, cache, services);
        Assert.True((await assignRole.InvokeAsync(new AssignRoleToUser { UserId = user, RoleName = role })).IsSuccess);
        Assert.IsType<ConflictError>((await assignRole.InvokeAsync(new AssignRoleToUser { UserId = user, RoleName = role })).Error);
        Assert.Equal([user], cache.Evicted);

        // Permissions again: added, never replacing, one grant per permission; the role's holder, found through its assignments, is evicted.
        Assert.True((await assignPermissions.InvokeAsync(new AssignPermissionsToRole { RoleName = role, Permissions = ["reports.read", "reports.sign"] })).IsSuccess);
        Assert.Equal(3, (await store.GetRolePermissionsAsync([role], null)).Count);
        Assert.Equal([user, user], cache.Evicted);

        // An assigned role grants its permissions on the next resolution.
        var provider = new ManagedRolePermissionProvider(store);
        Assert.Equal([permission, "reports.read", "reports.sign"], (await provider.ResolvePermissionsAsync(new TestUser(user))).Order(StringComparer.Ordinal));

        // List: the package's stores answer what was created, globally (the caller has no tenant, so not acme's role).
        var roles = await new ManagedDynamicRoleStore(store, admin).GetAllAsync();
        var listed = Assert.Single(roles, r => r.Name == role);
        Assert.Equal("Reads the reports", listed.Description);
        Assert.Equal([permission, "reports.read", "reports.sign"], listed.DefaultPermissions.Order(StringComparer.Ordinal));
        Assert.True(await new ManagedDynamicRoleStore(store, admin).ExistsAsync(role));
        Assert.Contains(await new ManagedDynamicPermissionStore(store, admin).GetAllAsync(), p => p.Name == permission && p.Category == "reports");

        // Revoke: the role stops granting, and revoking it again finds nothing to revoke.
        var revoke = new RevokeRoleFromUser.Invoker(store, admin, cache, services);
        Assert.True((await revoke.InvokeAsync(new RevokeRoleFromUser { UserId = user, RoleName = role })).IsSuccess);
        Assert.Empty(await provider.ResolvePermissionsAsync(new TestUser(user)));
        Assert.IsType<NotFoundError>((await revoke.InvokeAsync(new RevokeRoleFromUser { UserId = user, RoleName = role })).Error);
        Assert.Equal([user, user, user], cache.Evicted);

        // The revoked assignment is kept, ended: the history of who held the role.
        var history = Assert.Single(await store.GetUserRolesAsync(user, null));
        Assert.NotNull(history.ValidTo);
    }

    /// <summary>A permission cache that records whose permissions were evicted.</summary>
    private sealed class RecordingCache : IPermissionCacheInvalidator
    {
        public List<string> Evicted { get; } = [];

        public ValueTask InvalidateUserAsync(string userId, CancellationToken ct = default)
        {
            Evicted.Add(userId);
            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateTenantAsync(string tenantId, CancellationToken ct = default) => ValueTask.CompletedTask;

        public ValueTask InvalidateRoleAsync(string roleName, CancellationToken ct = default) => ValueTask.CompletedTask;

        public ValueTask InvalidateGroupAsync(string groupName, CancellationToken ct = default) => ValueTask.CompletedTask;
    }
}
