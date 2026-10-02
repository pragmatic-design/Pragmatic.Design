using Pragmatic.Testing.Assertions;
using Pragmatic.Identity.Persistence;
using Pragmatic.Identity.Persistence.Entities;

namespace Pragmatic.Identity.Persistence.Tests.Unit;

/// <summary>
///     #ID-P1: the app-layer active-row guard closes the gap left by non-enforcing providers.
///     <para>
///         <see cref="TemporalActiveUniquenessTests.InMemoryProvider_GetsNoFilter_FallsBackToNonUnique"/>
///         asserts (by design) that the InMemory fallback index is non-unique, so the DB itself will
///         happily persist a second active row. These tests prove the reusable guard
///         (<see cref="TemporalActiveRowExtensions"/>) detects and rejects that duplicate at the
///         application layer, which is where the config comments now point.
///     </para>
/// </summary>
public sealed class TemporalActiveRowGuardTests : IDisposable
{
    private readonly TestIdentityDbContext _db = TestIdentityDbContext.Create();
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task InMemory_SilentlyPersistsSecondActiveRow_WithoutTheGuard()
    {
        // Baseline: this is exactly the hole the guard exists to plug — InMemory accepts two active rows.
        var userId = Guid.NewGuid();
        _db.UserRoles.Add(new UserRole<Guid> { UserId = userId, RoleName = "admin", ValidFrom = Now });
        _db.UserRoles.Add(new UserRole<Guid> { UserId = userId, RoleName = "admin", ValidFrom = Now.AddDays(1) });

        var act = async () => await _db.SaveChangesAsync();

        await act.Should().NotThrowAsync("the InMemory fallback index is non-unique");
        _db.UserRoles.Count(ur => ur.RoleName == "admin" && ur.ValidTo == null).Should().Be(2);
    }

    [Fact]
    public async Task HasActiveUserRoleAsync_WhenActiveRowExists_ReturnsTrue()
    {
        var userId = Guid.NewGuid();
        _db.UserRoles.Add(new UserRole<Guid> { UserId = userId, RoleName = "admin", ValidFrom = Now });
        await _db.SaveChangesAsync();

        (await _db.HasActiveUserRoleAsync(userId, "admin")).Should().BeTrue();
        (await _db.HasActiveUserRoleAsync(userId, "editor")).Should().BeFalse();
    }

    [Fact]
    public async Task HasActiveUserRoleAsync_WhenOnlyExpiredRowExists_ReturnsFalse()
    {
        // An expired row (ValidTo set) does not occupy the active slot.
        var userId = Guid.NewGuid();
        _db.UserRoles.Add(new UserRole<Guid>
        {
            UserId = userId, RoleName = "admin", ValidFrom = Now.AddDays(-10), ValidTo = Now.AddDays(-1)
        });
        await _db.SaveChangesAsync();

        (await _db.HasActiveUserRoleAsync(userId, "admin")).Should().BeFalse();
    }

    [Fact]
    public async Task EnsureNoActiveUserRoleAsync_WhenActiveRowExists_ThrowsConflict()
    {
        var userId = Guid.NewGuid();
        _db.UserRoles.Add(new UserRole<Guid> { UserId = userId, RoleName = "admin", ValidFrom = Now });
        await _db.SaveChangesAsync();

        var act = async () => await _db.EnsureNoActiveUserRoleAsync(userId, "admin");

        (await act.Should().ThrowAsync<TemporalActiveRowConflictException>())
            .Which.ConflictDescription.Should().Contain("admin");
    }

    [Fact]
    public async Task EnsureNoActiveUserRoleAsync_WhenNoActiveRow_DoesNotThrow()
    {
        var userId = Guid.NewGuid();

        var act = async () => await _db.EnsureNoActiveUserRoleAsync(userId, "admin");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Guard_UsedBeforeInsert_PreventsSecondActiveRowOnInMemory()
    {
        // The intended usage: guard, then insert. The second attempt is rejected app-side even though
        // the InMemory provider would have persisted it.
        var userId = Guid.NewGuid();

        await _db.EnsureNoActiveUserRoleAsync(userId, "admin"); // passes — none yet
        _db.UserRoles.Add(new UserRole<Guid> { UserId = userId, RoleName = "admin", ValidFrom = Now });
        await _db.SaveChangesAsync();

        var secondInsert = async () =>
        {
            await _db.EnsureNoActiveUserRoleAsync(userId, "admin"); // now throws
            _db.UserRoles.Add(new UserRole<Guid> { UserId = userId, RoleName = "admin", ValidFrom = Now.AddDays(1) });
            await _db.SaveChangesAsync();
        };

        await secondInsert.Should().ThrowAsync<TemporalActiveRowConflictException>();
        _db.UserRoles.Count(ur => ur.RoleName == "admin" && ur.ValidTo == null).Should().Be(1);
    }

    [Fact]
    public async Task EnsureNoActiveRolePermissionAsync_WhenActiveRowExists_ThrowsConflict()
    {
        // Cover a non-generic-key join table too (RolePermission).
        _db.RolePermissions.Add(new RolePermission
        {
            RoleName = "admin", PermissionName = "users.create", ValidFrom = Now
        });
        await _db.SaveChangesAsync();

        var act = async () => await _db.EnsureNoActiveRolePermissionAsync("admin", "users.create");

        await act.Should().ThrowAsync<TemporalActiveRowConflictException>();
    }
}
