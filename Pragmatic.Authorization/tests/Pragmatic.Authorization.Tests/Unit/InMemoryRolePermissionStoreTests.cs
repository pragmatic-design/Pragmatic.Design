using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization.Stores;

namespace Pragmatic.Authorization.Tests.Unit;

public class InMemoryRolePermissionStoreTests
{
    [Fact]
    public async Task GetPermissionsForRoleAsync_ExistingRole_ReturnsPermissions()
    {
        var store = new InMemoryRolePermissionStore();
        store.AddRole("admin", ["orders.create", "orders.delete"]);

        var result = await store.GetPermissionsForRoleAsync("admin");

        result.Should().HaveCount(2);
        result.Should().Contain("orders.create");
        result.Should().Contain("orders.delete");
    }

    [Fact]
    public async Task GetPermissionsForRoleAsync_UnknownRole_ReturnsEmpty()
    {
        var store = new InMemoryRolePermissionStore();

        var result = await store.GetPermissionsForRoleAsync("unknown");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPermissionsForRoleAsync_CaseInsensitive()
    {
        var store = new InMemoryRolePermissionStore();
        store.AddRole("Admin", ["orders.create"]);

        var result = await store.GetPermissionsForRoleAsync("admin");

        result.Should().ContainSingle("orders.create");
    }

    [Fact]
    public async Task AddRole_MultipleCalls_MergesPermissions()
    {
        var store = new InMemoryRolePermissionStore();
        store.AddRole("admin", ["orders.create"]);
        store.AddRole("admin", ["orders.delete"]);

        var result = await store.GetPermissionsForRoleAsync("admin");

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllRolesAsync_ReturnsAllRegisteredRoles()
    {
        var store = new InMemoryRolePermissionStore();
        store.AddRole("admin", ["orders.create"]);
        store.AddRole("viewer", ["orders.read"]);

        var roles = await store.GetAllRolesAsync();

        roles.Should().HaveCount(2);
        roles.Should().Contain("admin");
        roles.Should().Contain("viewer");
    }

    [Fact]
    public async Task GetAllRolesAsync_Empty_ReturnsEmpty()
    {
        var store = new InMemoryRolePermissionStore();

        var roles = await store.GetAllRolesAsync();

        roles.Should().BeEmpty();
    }
}
