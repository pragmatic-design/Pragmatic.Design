using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Identity.Persistence.Entities;

namespace Pragmatic.Identity.Persistence.Tests.Unit;

/// <summary>
///     Proves the provider-aware "only one ACTIVE row" invariant on temporal Identity tables.
///     <para>
///         The active-uniqueness rule is a FILTERED unique index (<c>WHERE ValidTo IS NULL</c>).
///         The EF InMemory provider ignores indexes, so enforcement is verified against SQLite,
///         which honors filtered unique indexes. Model-level assertions additionally confirm the
///         index metadata (unique + filter) is present.
///     </para>
/// </summary>
public sealed class TemporalActiveUniquenessTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    // --- SQLite enforcement: a SECOND active row for the same key must be rejected. ---

    [Fact]
    public async Task RolePermission_SecondActiveRow_IsRejectedBySqlite()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = TestIdentityDbContext.CreateSqlite(connection);

        db.RolePermissions.Add(new RolePermission
        {
            RoleName = "admin",
            PermissionName = "users.create",
            ValidFrom = Now
        });
        await db.SaveChangesAsync();

        // Second ACTIVE (ValidTo == null) row for the same role+permission.
        db.RolePermissions.Add(new RolePermission
        {
            RoleName = "admin",
            PermissionName = "users.create",
            ValidFrom = Now.AddDays(1)
        });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task RolePermission_ExpiredPlusNewActive_IsAllowed()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = TestIdentityDbContext.CreateSqlite(connection);

        // Expired row (ValidTo set) does not occupy the active slot.
        db.RolePermissions.Add(new RolePermission
        {
            RoleName = "admin",
            PermissionName = "users.create",
            ValidFrom = Now.AddDays(-10),
            ValidTo = Now.AddDays(-1)
        });
        // A single new active row is fine.
        db.RolePermissions.Add(new RolePermission
        {
            RoleName = "admin",
            PermissionName = "users.create",
            ValidFrom = Now
        });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UserRole_SecondActiveRow_IsRejectedBySqlite()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = TestIdentityDbContext.CreateSqlite(connection);

        var userId = Guid.NewGuid();
        db.UserRoles.Add(new UserRole<Guid> { UserId = userId, RoleName = "admin", ValidFrom = Now });
        await db.SaveChangesAsync();

        db.UserRoles.Add(new UserRole<Guid> { UserId = userId, RoleName = "admin", ValidFrom = Now.AddDays(1) });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UserGroup_SecondActiveRow_IsRejectedBySqlite()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = TestIdentityDbContext.CreateSqlite(connection);

        var userId = Guid.NewGuid();
        db.UserGroups.Add(new UserGroup<Guid> { UserId = userId, GroupName = "engineering", ValidFrom = Now });
        await db.SaveChangesAsync();

        db.UserGroups.Add(new UserGroup<Guid> { UserId = userId, GroupName = "engineering", ValidFrom = Now.AddDays(1) });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task GroupRole_SecondActiveRow_IsRejectedBySqlite()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = TestIdentityDbContext.CreateSqlite(connection);

        db.GroupRoles.Add(new GroupRole { GroupName = "engineering", RoleName = "developer", ValidFrom = Now });
        await db.SaveChangesAsync();

        db.GroupRoles.Add(new GroupRole { GroupName = "engineering", RoleName = "developer", ValidFrom = Now.AddDays(1) });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // --- CHECK constraint: ValidTo must be null or >= ValidFrom. ---

    [Fact]
    public async Task RolePermission_ValidToBeforeValidFrom_IsRejectedBySqlite()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = TestIdentityDbContext.CreateSqlite(connection);

        db.RolePermissions.Add(new RolePermission
        {
            RoleName = "admin",
            PermissionName = "users.create",
            ValidFrom = Now,
            ValidTo = Now.AddDays(-1) // inverted range
        });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // --- Model metadata: filtered unique index + check constraint exist (provider-agnostic). ---

    [Fact]
    public void RolePermission_ActiveIndex_IsUniqueAndFiltered_OnSqlite()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = TestIdentityDbContext.CreateSqlite(connection);

        var entityType = db.Model.FindEntityType(typeof(RolePermission))!;
        var activeIndex = entityType.GetIndexes()
            .Single(i => i.GetDatabaseName() == "UX_RolePermissions_Role_Permission_Active");

        activeIndex.IsUnique.Should().BeTrue();
        activeIndex.GetFilter().Should().Be("\"ValidTo\" IS NULL");
    }

    [Fact]
    public void TemporalTables_HaveValidRangeCheckConstraint_OnSqlite()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = TestIdentityDbContext.CreateSqlite(connection);

        // Assert against the REAL created schema (sqlite_master) — robust and provider-true, and proves
        // the constraint actually reached the table rather than just existing on the model.
        AssertHasCheck(connection, "RolePermissions", "CK_RolePermissions_ValidRange");
        AssertHasCheck(connection, "GroupRoles", "CK_GroupRoles_ValidRange");
        AssertHasCheck(connection, "UserRoles", "CK_UserRoles_ValidRange");
        AssertHasCheck(connection, "UserGroups", "CK_UserGroups_ValidRange");

        static void AssertHasCheck(SqliteConnection connection, string table, string constraintName)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $name";
            cmd.Parameters.AddWithValue("$name", table);
            var createSql = cmd.ExecuteScalar() as string;
            createSql.Should().NotBeNull($"table {table} should exist");
            createSql.Should().Contain(constraintName, $"table {table} must carry the {constraintName} CHECK constraint");
        }
    }

    [Fact]
    public void InMemoryProvider_GetsNoFilter_FallsBackToNonUnique()
    {
        // Confirms the documented fallback: with no relational provider the active index is a
        // plain (non-unique, non-filtered) lookup index, so legitimate history is never rejected.
        using var db = TestIdentityDbContext.Create();

        var entityType = db.Model.FindEntityType(typeof(RolePermission))!;
        var activeIndex = entityType.GetIndexes()
            .Single(i => i.GetDatabaseName() == "UX_RolePermissions_Role_Permission_Active");

        activeIndex.IsUnique.Should().BeFalse();
    }
}
