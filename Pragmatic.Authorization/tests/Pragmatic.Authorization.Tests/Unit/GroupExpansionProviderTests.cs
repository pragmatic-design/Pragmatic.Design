using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization.Providers;
using Pragmatic.Authorization.Stores;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Tests.Unit;

public class GroupExpansionProviderTests
{
    private sealed class TestUser(Dictionary<string, IReadOnlyList<string>> claims) : ICurrentUser
    {
        public string Id => "test";
        public string? DisplayName => "Test";
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => claims;
        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    [Fact]
    public async Task ResolvePermissionsAsync_GroupToRoleToPermission_ReturnsPermissions()
    {
        var groupStore = new InMemoryGroupRoleStore();
        groupStore.AddGroup("engineering", ["developer"]);

        var roleStore = new InMemoryRolePermissionStore();
        roleStore.AddRole("developer", ["code.read", "code.write"]);

        var provider = new GroupExpansionProvider(groupStore, roleStore);
        var user = new TestUser(new Dictionary<string, IReadOnlyList<string>>
        {
            ["group"] = ["engineering"]
        });

        var permissions = await provider.ResolvePermissionsAsync(user);

        permissions.Should().Contain("code.read").And.Contain("code.write");
    }

    [Fact]
    public async Task ResolvePermissionsAsync_NoGroups_ReturnsEmpty()
    {
        var groupStore = new InMemoryGroupRoleStore();
        var roleStore = new InMemoryRolePermissionStore();

        var provider = new GroupExpansionProvider(groupStore, roleStore);
        var user = new TestUser(new Dictionary<string, IReadOnlyList<string>>());

        var permissions = await provider.ResolvePermissionsAsync(user);

        permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolvePermissionsAsync_MultipleGroupsWithOverlap_DeduplicatesPermissions()
    {
        var groupStore = new InMemoryGroupRoleStore();
        groupStore.AddGroup("engineering", ["developer"]);
        groupStore.AddGroup("qa", ["tester", "developer"]);

        var roleStore = new InMemoryRolePermissionStore();
        roleStore.AddRole("developer", ["code.read", "code.write"]);
        roleStore.AddRole("tester", ["test.run", "code.read"]);

        var provider = new GroupExpansionProvider(groupStore, roleStore);
        var user = new TestUser(new Dictionary<string, IReadOnlyList<string>>
        {
            ["group"] = ["engineering", "qa"]
        });

        var permissions = await provider.ResolvePermissionsAsync(user);

        permissions.Should().HaveCount(3);
        permissions.Should().Contain("code.read")
            .And.Contain("code.write")
            .And.Contain("test.run");
    }

    [Fact]
    public async Task ResolvePermissionsAsync_UnknownGroup_ReturnsEmpty()
    {
        var groupStore = new InMemoryGroupRoleStore();
        var roleStore = new InMemoryRolePermissionStore();

        var provider = new GroupExpansionProvider(groupStore, roleStore);
        var user = new TestUser(new Dictionary<string, IReadOnlyList<string>>
        {
            ["group"] = ["nonexistent"]
        });

        var permissions = await provider.ResolvePermissionsAsync(user);

        permissions.Should().BeEmpty();
    }

    [Fact]
    public void Order_Is200()
    {
        var provider = new GroupExpansionProvider(
            new InMemoryGroupRoleStore(),
            new InMemoryRolePermissionStore());

        provider.Order.Should().Be(200);
    }
}
