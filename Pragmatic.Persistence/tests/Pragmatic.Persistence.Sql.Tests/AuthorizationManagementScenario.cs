using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Management.Actions.Assignments;
using Pragmatic.Authorization.Management.Actions.Groups;
using Pragmatic.Authorization.Management.Actions.Permissions;
using Pragmatic.Authorization.Management.Actions.Roles;
using Pragmatic.Authorization.Management.Entities;
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
        // An empty catalog: the scenario measures the engine's store, and a name the catalog holds is the Showcase's to prove.
        var createPermission = new CreatePermission.Invoker(store, new Pragmatic.Authorization.Catalog.DefaultPermissionCatalog(), admin, services);
        Assert.True((await createPermission.InvokeAsync(new CreatePermission { Name = permission, Category = "reports" })).IsSuccess);
        Assert.IsType<ConflictError>((await createPermission.InvokeAsync(new CreatePermission { Name = permission })).Error);

        var createRole = new CreateRole.Invoker(store, admin, services);
        Assert.True((await createRole.InvokeAsync(new CreateRole { Name = role, Description = "Reads the reports" })).IsSuccess);
        Assert.IsType<ConflictError>((await createRole.InvokeAsync(new CreateRole { Name = role })).Error);
        Assert.True((await createRole.InvokeAsync(new CreateRole { Name = role, TenantId = "acme-" + suffix })).IsSuccess);

        // The database refuses a name taken twice in a tenant too, when the row is written past the operation's check (two requests that
        // both passed it): the unique index on (Name, TenantId) (#490). In a tenant, because a NULL TenantId is distinct in an index on
        // PostgreSQL, SQLite and MySQL.
        AssertRefusedByTheUniqueIndex(await Assert.ThrowsAnyAsync<Exception>(async () =>
            await store.AddRoleAsync(new DynamicRole { Name = role, TenantId = "acme-" + suffix })), "DynamicRoles");
        await store.AddPermissionAsync(new DynamicPermission { Name = permission, TenantId = "acme-" + suffix });
        AssertRefusedByTheUniqueIndex(await Assert.ThrowsAnyAsync<Exception>(async () =>
            await store.AddPermissionAsync(new DynamicPermission { Name = permission, TenantId = "acme-" + suffix })), "DynamicPermissions");

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

        // And its name is among the caller's roles, so a role check agrees with the permissions (#723): through the resolver the host
        // registers, with the package's role provider.
        CachedPermissionResolver Resolver() => new([provider], new TestUser(user), roleProviders: [new ManagedRoleProvider(store)]);
        Assert.Contains(role, Resolver().Roles);
        Assert.True(Resolver().IsInRole(role));

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
        Assert.DoesNotContain(role, Resolver().Roles); // the control: a revoked role is no longer the caller's
        Assert.IsType<NotFoundError>((await revoke.InvokeAsync(new RevokeRoleFromUser { UserId = user, RoleName = role })).Error);
        Assert.Equal([user, user, user], cache.Evicted);

        // The revoked assignment is kept, ended: the history of who held the role.
        var history = Assert.Single(await store.GetUserRolesAsync(user, null));
        Assert.NotNull(history.ValidTo);

        await RunGroupsAsync(store, admin, services, role, suffix);
    }

    /// <summary>
    ///     Groups (#726): a group is created once per tenant, is given the role, and its member holds the role and its permissions on the next
    ///     resolution, until the membership ends; a change to what the role grants evicts the member, found through the group.
    /// </summary>
    private static async Task RunGroupsAsync(IAuthorizationManagementStore store, TestUser admin, IServiceProvider services, string role, string suffix)
    {
        var group = "night-shift-" + suffix;
        var member = "grace-" + suffix;

        var createGroup = new CreateGroup.Invoker(store, admin, services);
        Assert.True((await createGroup.InvokeAsync(new CreateGroup { Name = group, Description = "Covers the night" })).IsSuccess);
        Assert.IsType<ConflictError>((await createGroup.InvokeAsync(new CreateGroup { Name = group })).Error);
        AssertRefusedByTheUniqueIndex(await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await store.AddGroupAsync(new DynamicGroup { Name = group, TenantId = "acme-" + suffix });
            await store.AddGroupAsync(new DynamicGroup { Name = group, TenantId = "acme-" + suffix });
        }), "DynamicGroups");

        var cache = new RecordingCache();
        Assert.True((await new AssignRoleToGroup.Invoker(store, admin, cache, services)
            .InvokeAsync(new AssignRoleToGroup { GroupName = group, RoleName = role })).IsSuccess);
        Assert.Empty(cache.Evicted); // nobody is a member yet

        var addMember = new AssignUserToGroup.Invoker(store, admin, cache, services);
        Assert.True((await addMember.InvokeAsync(new AssignUserToGroup { UserId = member, GroupName = group })).IsSuccess);
        Assert.IsType<ConflictError>((await addMember.InvokeAsync(new AssignUserToGroup { UserId = member, GroupName = group })).Error);
        Assert.Equal([member], cache.Evicted);

        // The member holds the group's role, by name and by what it grants.
        var provider = new ManagedRolePermissionProvider(store);
        Assert.Equal([$"reports.export.{suffix}", "reports.read", "reports.sign"], (await provider.ResolvePermissionsAsync(new TestUser(member))).Order(StringComparer.Ordinal));
        CachedPermissionResolver Resolver() => new([provider], new TestUser(member), roleProviders: [new ManagedRoleProvider(store)]);
        Assert.True(Resolver().IsInRole(role));
        var listed = (await new ListGroups.Invoker(store, admin, services).InvokeAsync(new ListGroups())).Value;
        Assert.Equal([role], Assert.Single(listed, g => g.Name == group).Roles);

        // A grant added to the role reaches the member through the group, so the member's cached permissions are evicted.
        Assert.True((await new AssignPermissionsToRole.Invoker(store, admin, cache, services)
            .InvokeAsync(new AssignPermissionsToRole { RoleName = role, Permissions = ["reports.archive"] })).IsSuccess);
        Assert.Equal([member, member], cache.Evicted);

        // Removed, the member holds nothing from the group, and removing again finds nothing.
        var remove = new RemoveUserFromGroup.Invoker(store, admin, cache, services);
        Assert.True((await remove.InvokeAsync(new RemoveUserFromGroup { UserId = member, GroupName = group })).IsSuccess);
        Assert.Empty(await provider.ResolvePermissionsAsync(new TestUser(member)));
        Assert.False(Resolver().IsInRole(role));
        Assert.IsType<NotFoundError>((await remove.InvokeAsync(new RemoveUserFromGroup { UserId = member, GroupName = group })).Error);
        Assert.NotNull(Assert.Single(await store.GetUserGroupsAsync(member, null)).ValidTo); // kept, ended

        // Taking the role back from the group ends it, and a second time finds nothing.
        var revoke = new RevokeRoleFromGroup.Invoker(store, admin, cache, services);
        Assert.True((await revoke.InvokeAsync(new RevokeRoleFromGroup { GroupName = group, RoleName = role })).IsSuccess);
        Assert.IsType<NotFoundError>((await revoke.InvokeAsync(new RevokeRoleFromGroup { GroupName = group, RoleName = role })).Error);
    }

    /// <summary>
    ///     The write was refused by the unique index on <paramref name="table" />'s (Name, TenantId): its name in the database's message, or,
    ///     on SQLite, which names the columns instead, the two columns.
    /// </summary>
    private static void AssertRefusedByTheUniqueIndex(Exception refused, string table)
    {
        var text = refused.ToString();
        Assert.True(text.Contains($"IX_{table}_Name_TenantId", StringComparison.OrdinalIgnoreCase)
                    || text.Contains($"{table}.Name, {table}.TenantId", StringComparison.Ordinal), text);
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
