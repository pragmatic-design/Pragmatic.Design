using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Identity.Persistence.Entities;
using Pragmatic.Identity.Persistence.Stores;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Persistence.Tests.Unit;

public sealed class EfRolePermissionStoreTests : IDisposable
{
    private readonly TestIdentityDbContext _db = TestIdentityDbContext.Create();
    private readonly ClockMock _clock = new ClockMock();
    private readonly DateTimeOffset _now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    public EfRolePermissionStoreTests()
    {
        _clock.UtcNow.Returns(_now);
    }

    public void Dispose() => _db.Dispose();

    private EfRolePermissionStore CreateStore() => new(_db, _clock);

    [Fact]
    public async Task GetPermissionsForRoleAsync_WithActivePermissions_ReturnsMatching()
    {
        _db.RolePermissions.AddRange(
            new RolePermission { RoleName = "admin", PermissionName = "users.create", ValidFrom = _now.AddDays(-10) },
            new RolePermission { RoleName = "admin", PermissionName = "users.delete", ValidFrom = _now.AddDays(-5) },
            new RolePermission { RoleName = "viewer", PermissionName = "users.read", ValidFrom = _now.AddDays(-3) });
        await _db.SaveChangesAsync();

        var store = CreateStore();
        var result = await store.GetPermissionsForRoleAsync("admin");

        result.Should().BeEquivalentTo(["users.create", "users.delete"]);
    }

    [Fact]
    public async Task GetPermissionsForRoleAsync_WithExpiredPermissions_ExcludesThem()
    {
        _db.RolePermissions.AddRange(
            new RolePermission { RoleName = "admin", PermissionName = "users.create", ValidFrom = _now.AddDays(-10) },
            new RolePermission { RoleName = "admin", PermissionName = "old.perm", ValidFrom = _now.AddDays(-30), ValidTo = _now.AddDays(-1) });
        await _db.SaveChangesAsync();

        var store = CreateStore();
        var result = await store.GetPermissionsForRoleAsync("admin");

        result.Should().BeEquivalentTo(["users.create"]);
    }

    [Fact]
    public async Task GetPermissionsForRoleAsync_WithFuturePermissions_ExcludesThem()
    {
        _db.RolePermissions.AddRange(
            new RolePermission { RoleName = "admin", PermissionName = "users.create", ValidFrom = _now.AddDays(-1) },
            new RolePermission { RoleName = "admin", PermissionName = "future.perm", ValidFrom = _now.AddDays(5) });
        await _db.SaveChangesAsync();

        var store = CreateStore();
        var result = await store.GetPermissionsForRoleAsync("admin");

        result.Should().BeEquivalentTo(["users.create"]);
    }

    [Fact]
    public async Task GetPermissionsForRoleAsync_TemporalOverload_FiltersAtSpecificTime()
    {
        var past = _now.AddDays(-20);
        _db.RolePermissions.AddRange(
            new RolePermission { RoleName = "admin", PermissionName = "old.perm", ValidFrom = past.AddDays(-10), ValidTo = past.AddDays(5) },
            new RolePermission { RoleName = "admin", PermissionName = "new.perm", ValidFrom = _now.AddDays(-1) });
        await _db.SaveChangesAsync();

        var store = CreateStore();
        var result = await store.GetPermissionsForRoleAsync("admin", past);

        result.Should().BeEquivalentTo(["old.perm"]);
    }

    [Fact]
    public async Task GetPermissionsForRoleAsync_UnknownRole_ReturnsEmpty()
    {
        var store = CreateStore();
        var result = await store.GetPermissionsForRoleAsync("nonexistent");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllRolesAsync_ReturnsDistinctActiveRoles()
    {
        _db.RolePermissions.AddRange(
            new RolePermission { RoleName = "admin", PermissionName = "p1", ValidFrom = _now.AddDays(-5) },
            new RolePermission { RoleName = "admin", PermissionName = "p2", ValidFrom = _now.AddDays(-3) },
            new RolePermission { RoleName = "viewer", PermissionName = "p3", ValidFrom = _now.AddDays(-1) },
            new RolePermission { RoleName = "expired", PermissionName = "p4", ValidFrom = _now.AddDays(-30), ValidTo = _now.AddDays(-10) });
        await _db.SaveChangesAsync();

        var store = CreateStore();
        var result = await store.GetAllRolesAsync();

        result.Should().BeEquivalentTo(["admin", "viewer"]);
    }

    [Fact]
    public async Task GetPermissionsForRoleAsync_CaseInsensitiveResults()
    {
        _db.RolePermissions.AddRange(
            new RolePermission { RoleName = "admin", PermissionName = "Users.Create", ValidFrom = _now.AddDays(-1) },
            new RolePermission { RoleName = "admin", PermissionName = "users.create", ValidFrom = _now.AddDays(-1) });
        await _db.SaveChangesAsync();

        var store = CreateStore();
        var result = await store.GetPermissionsForRoleAsync("admin");

        // Case-insensitive set should deduplicate
        result.Should().HaveCount(1);
    }
}
