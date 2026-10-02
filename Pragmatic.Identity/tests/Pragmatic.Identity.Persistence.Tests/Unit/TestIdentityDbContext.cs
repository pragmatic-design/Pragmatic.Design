using Microsoft.EntityFrameworkCore;
using Pragmatic.Identity.Persistence.Configuration;
using Pragmatic.Identity.Persistence.Entities;

namespace Pragmatic.Identity.Persistence.Tests.Unit;

/// <summary>
///     In-memory DbContext for Identity persistence tests.
/// </summary>
public sealed class TestIdentityDbContext(DbContextOptions<TestIdentityDbContext> options) : DbContext(options)
{
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<GroupRole> GroupRoles => Set<GroupRole>();
    public DbSet<UserRole<Guid>> UserRoles => Set<UserRole<Guid>>();
    public DbSet<UserGroup<Guid>> UserGroups => Set<UserGroup<Guid>>();
    public DbSet<ExternalIdentityRecord<Guid>> ExternalIdentityRecords => Set<ExternalIdentityRecord<Guid>>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // No user entity of its own, and no inheritance base to derive one from: the application
        // shape comes from [PragmaticUser] with an owned identity record, and the stores under
        // test here — role permissions, group roles — never needed to know the user type.
        modelBuilder.ApplyIdentityConfigurations<ExternalIdentityRecord<Guid>, Guid>(Database.ProviderName);
    }

    public static TestIdentityDbContext Create(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<TestIdentityDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;

        var context = new TestIdentityDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    ///     Creates a SQLite-backed context over the given open connection. SQLite enforces
    ///     unique indexes (including filtered/partial ones), unlike the EF InMemory provider,
    ///     so it is used to prove the active-uniqueness constraint actually rejects rows.
    ///     The caller owns the connection lifetime (keep it open for the test's duration).
    /// </summary>
    public static TestIdentityDbContext CreateSqlite(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        bool ensureCreated = true,
        params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<TestIdentityDbContext>()
            .UseSqlite(connection);

        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);

        var context = new TestIdentityDbContext(builder.Options);
        if (ensureCreated)
            context.Database.EnsureCreated();
        return context;
    }
}
