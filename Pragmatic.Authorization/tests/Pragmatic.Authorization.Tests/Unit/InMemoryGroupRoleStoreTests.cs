using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization.Stores;

namespace Pragmatic.Authorization.Tests.Unit;

public class InMemoryGroupRoleStoreTests
{
    [Fact]
    public async Task GetRolesForGroupAsync_ExistingGroup_ReturnsRoles()
    {
        var store = new InMemoryGroupRoleStore();
        store.AddGroup("engineering", ["developer", "reviewer"]);

        var roles = await store.GetRolesForGroupAsync("engineering");

        roles.Should().Contain("developer").And.Contain("reviewer");
    }

    [Fact]
    public async Task GetRolesForGroupAsync_NonExistentGroup_ReturnsEmpty()
    {
        var store = new InMemoryGroupRoleStore();

        var roles = await store.GetRolesForGroupAsync("unknown");

        roles.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRolesForGroupAsync_CaseInsensitive()
    {
        var store = new InMemoryGroupRoleStore();
        store.AddGroup("Engineering", ["developer"]);

        var roles = await store.GetRolesForGroupAsync("engineering");

        roles.Should().ContainSingle("developer");
    }

    [Fact]
    public async Task GetAllGroupsAsync_ReturnsAllGroups()
    {
        var store = new InMemoryGroupRoleStore();
        store.AddGroup("engineering", ["developer"]);
        store.AddGroup("sales", ["account-manager"]);

        var groups = await store.GetAllGroupsAsync();

        groups.Should().HaveCount(2);
        groups.Should().Contain("engineering").And.Contain("sales");
    }

    [Fact]
    public async Task GetAllGroupsAsync_Empty_ReturnsEmpty()
    {
        var store = new InMemoryGroupRoleStore();

        var groups = await store.GetAllGroupsAsync();

        groups.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRolesForGroupAsync_MutatingReturnedSet_DoesNotAffectStore()
    {
        var store = new InMemoryGroupRoleStore();
        store.AddGroup("engineering", ["developer"]);

        var roles = await store.GetRolesForGroupAsync("engineering");

        // The store must hand back a defensive copy, not its live mutable backing set.
        ((HashSet<string>)roles).Add("intruder");

        var fresh = await store.GetRolesForGroupAsync("engineering");
        fresh.Should().ContainSingle().Which.Should().Be("developer");
        fresh.Should().NotContain("intruder");
    }

    [Fact]
    public async Task GetRolesForGroupAsync_MissingGroup_ReturnsEmptySet()
    {
        var store = new InMemoryGroupRoleStore();

        var roles = await store.GetRolesForGroupAsync("missing");

        roles.Should().NotBeNull();
        roles.Should().BeEmpty();
    }

    [Fact]
    public async Task AddGroup_MergesRoles()
    {
        var store = new InMemoryGroupRoleStore();
        store.AddGroup("engineering", ["developer"]);
        store.AddGroup("engineering", ["reviewer"]);

        var roles = await store.GetRolesForGroupAsync("engineering");

        roles.Should().HaveCount(2);
        roles.Should().Contain("developer").And.Contain("reviewer");
    }
}
