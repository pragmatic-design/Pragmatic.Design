using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Identity.Persistence.Entities;
using Pragmatic.Identity.Persistence.Stores;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Persistence.Tests.Unit;

public sealed class EfGroupRoleStoreTests : IDisposable
{
    private readonly TestIdentityDbContext _db = TestIdentityDbContext.Create();
    private readonly ClockMock _clock = new ClockMock();
    private readonly DateTimeOffset _now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    public EfGroupRoleStoreTests()
    {
        _clock.UtcNow.Returns(_now);
    }

    public void Dispose() => _db.Dispose();

    private EfGroupRoleStore CreateStore() => new(_db, _clock);

    [Fact]
    public async Task GetRolesForGroupAsync_WithActiveRoles_ReturnsMatching()
    {
        _db.GroupRoles.AddRange(
            new GroupRole { GroupName = "engineering", RoleName = "developer", ValidFrom = _now.AddDays(-10) },
            new GroupRole { GroupName = "engineering", RoleName = "reviewer", ValidFrom = _now.AddDays(-5) },
            new GroupRole { GroupName = "sales", RoleName = "viewer", ValidFrom = _now.AddDays(-3) });
        await _db.SaveChangesAsync();

        var store = CreateStore();
        var result = await store.GetRolesForGroupAsync("engineering");

        result.Should().BeEquivalentTo(["developer", "reviewer"]);
    }

    [Fact]
    public async Task GetRolesForGroupAsync_WithExpiredRoles_ExcludesThem()
    {
        _db.GroupRoles.AddRange(
            new GroupRole { GroupName = "engineering", RoleName = "developer", ValidFrom = _now.AddDays(-10) },
            new GroupRole { GroupName = "engineering", RoleName = "old-role", ValidFrom = _now.AddDays(-30), ValidTo = _now.AddDays(-5) });
        await _db.SaveChangesAsync();

        var store = CreateStore();
        var result = await store.GetRolesForGroupAsync("engineering");

        result.Should().BeEquivalentTo(["developer"]);
    }

    [Fact]
    public async Task GetRolesForGroupAsync_TemporalOverload_FiltersAtSpecificTime()
    {
        var past = _now.AddDays(-20);
        _db.GroupRoles.AddRange(
            new GroupRole { GroupName = "engineering", RoleName = "old-role", ValidFrom = past.AddDays(-10), ValidTo = past.AddDays(5) },
            new GroupRole { GroupName = "engineering", RoleName = "new-role", ValidFrom = _now.AddDays(-1) });
        await _db.SaveChangesAsync();

        var store = CreateStore();
        var result = await store.GetRolesForGroupAsync("engineering", past);

        result.Should().BeEquivalentTo(["old-role"]);
    }

    [Fact]
    public async Task GetRolesForGroupAsync_UnknownGroup_ReturnsEmpty()
    {
        var store = CreateStore();
        var result = await store.GetRolesForGroupAsync("nonexistent");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllGroupsAsync_ReturnsDistinctActiveGroups()
    {
        _db.GroupRoles.AddRange(
            new GroupRole { GroupName = "engineering", RoleName = "dev", ValidFrom = _now.AddDays(-5) },
            new GroupRole { GroupName = "engineering", RoleName = "reviewer", ValidFrom = _now.AddDays(-3) },
            new GroupRole { GroupName = "sales", RoleName = "viewer", ValidFrom = _now.AddDays(-1) },
            new GroupRole { GroupName = "expired-group", RoleName = "old", ValidFrom = _now.AddDays(-30), ValidTo = _now.AddDays(-10) });
        await _db.SaveChangesAsync();

        var store = CreateStore();
        var result = await store.GetAllGroupsAsync();

        result.Should().BeEquivalentTo(["engineering", "sales"]);
    }

    [Fact]
    public async Task GetRolesForGroupAsync_CaseInsensitiveResults()
    {
        _db.GroupRoles.AddRange(
            new GroupRole { GroupName = "engineering", RoleName = "Developer", ValidFrom = _now.AddDays(-1) },
            new GroupRole { GroupName = "engineering", RoleName = "developer", ValidFrom = _now.AddDays(-1) });
        await _db.SaveChangesAsync();

        var store = CreateStore();
        var result = await store.GetRolesForGroupAsync("engineering");

        // Case-insensitive set should deduplicate
        result.Should().HaveCount(1);
    }
}
