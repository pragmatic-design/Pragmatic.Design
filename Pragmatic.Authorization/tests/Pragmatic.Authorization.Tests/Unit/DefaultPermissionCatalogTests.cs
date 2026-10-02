using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization.Catalog;
using Pragmatic.Authorization.Stores;

namespace Pragmatic.Authorization.Tests.Unit;

public class DefaultPermissionCatalogTests
{
    // =========================================================================
    // Test doubles
    // =========================================================================

    private sealed class FakeDynamicPermissionStore(params PermissionInfo[] permissions) : IDynamicPermissionStore
    {
        public ValueTask<IReadOnlyList<PermissionInfo>> GetAllAsync(CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<PermissionInfo>>(permissions);

        public ValueTask<bool> ExistsAsync(string permissionName, CancellationToken ct = default)
            => ValueTask.FromResult(permissions.Any(p => p.Name == permissionName));
    }

    private sealed class FakeDynamicRoleStore(params RoleInfo[] roles) : IDynamicRoleStore
    {
        public ValueTask<IReadOnlyList<RoleInfo>> GetAllAsync(CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<RoleInfo>>(roles);

        public ValueTask<bool> ExistsAsync(string roleName, CancellationToken ct = default)
            => ValueTask.FromResult(roles.Any(r => r.Name == roleName));
    }

    // =========================================================================
    // GetAllPermissionsAsync
    // =========================================================================

    [Fact]
    public async Task GetAllPermissions_StaticOnly_ReturnsStaticPermissions()
    {
        var staticPerms = new List<PermissionInfo>
        {
            new("orders.read", "Read orders", "orders"),
            new("orders.write", "Write orders", "orders")
        };
        var catalog = new DefaultPermissionCatalog(staticPermissions: staticPerms);

        var result = await catalog.GetAllPermissionsAsync();

        result.Should().HaveCount(2);
        result.Should().Contain(p => p.Name == "orders.read");
    }

    [Fact]
    public async Task GetAllPermissions_NullStatic_ReturnsEmpty()
    {
        var catalog = new DefaultPermissionCatalog();

        var result = await catalog.GetAllPermissionsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllPermissions_MergesStaticAndDynamic()
    {
        var staticPerms = new List<PermissionInfo>
        {
            new("orders.read", null, "orders")
        };
        var dynamicStore = new FakeDynamicPermissionStore(
            new PermissionInfo("orders.approve", "Approve orders", "orders"));

        var catalog = new DefaultPermissionCatalog(
            staticPermissions: staticPerms,
            dynamicPermissionStore: dynamicStore);

        var result = await catalog.GetAllPermissionsAsync();

        result.Should().HaveCount(2);
        result.Should().Contain(p => p.Name == "orders.read");
        result.Should().Contain(p => p.Name == "orders.approve");
    }

    // =========================================================================
    // GetAllRolesAsync
    // =========================================================================

    [Fact]
    public async Task GetAllRoles_StaticOnly_ReturnsStaticRoles()
    {
        var staticRoles = new List<RoleInfo>
        {
            new("admin", "Administrator", ["*"])
        };
        var catalog = new DefaultPermissionCatalog(staticRoles: staticRoles);

        var result = await catalog.GetAllRolesAsync();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("admin");
    }

    [Fact]
    public async Task GetAllRoles_MergesStaticAndDynamic()
    {
        var staticRoles = new List<RoleInfo>
        {
            new("admin", null, ["*"])
        };
        var dynamicStore = new FakeDynamicRoleStore(
            new RoleInfo("editor", "Editor role", ["orders.read", "orders.write"]));

        var catalog = new DefaultPermissionCatalog(
            staticRoles: staticRoles,
            dynamicRoleStore: dynamicStore);

        var result = await catalog.GetAllRolesAsync();

        result.Should().HaveCount(2);
    }
}
