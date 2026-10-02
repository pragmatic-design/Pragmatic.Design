using Microsoft.EntityFrameworkCore;
using Pragmatic.Authorization.Stores;
using Pragmatic.Identity.Persistence.Entities;
using Pragmatic.Identity.Persistence.Stores;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Testing;

namespace Pragmatic.Identity.Samples.Samples;

/// <summary>
///     Identity.Persistence — runnable temporal role and group stores over a real EF Core
///     <see cref="DbContext"/> (in-memory provider).
///
///     <see cref="EfRolePermissionStore"/> and <see cref="EfGroupRoleStore"/> run here against
///     EF Core's in-memory provider — a host swaps it for SQL Server / Npgsql.
///     The temporal filtering (ValidFrom/ValidTo vs <see cref="IClock"/>) is the real logic:
///     we use the real <see cref="TestClock"/> and advance it to show a future grant activating.
/// </summary>
public static class PersistenceStoresSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("8. Identity.Persistence — Temporal role and group stores");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        RunAsync().GetAwaiter().GetResult();

        Console.WriteLine();
    }

    private static async Task RunAsync()
    {
        var now = DateTimeOffset.UtcNow;
        // Real TestClock from Pragmatic.Temporal.Testing — supports Advance() to move time forward.
        var clock = new TestClock(now);

        await using var db = NewContext();

        await SeedTemporalMappings(db, now);

        await ShowRolePermissionStore(db, clock);
        await ShowGroupRoleStore(db, clock);
    }

    // ===== 8.1 EfRolePermissionStore ========================================
    private static async Task ShowRolePermissionStore(SampleIdentityDbContext db, TestClock clock)
    {
        Console.WriteLine("  8.1 EfRolePermissionStore — temporal role → permissions");
        Console.WriteLine("  ---------------------------------------------------------");

        var store = new EfRolePermissionStore(db, clock);

        // The store reads 'now' from the injected clock and filters by ValidFrom/ValidTo.
        var editorNow = await store.GetPermissionsForRoleAsync("docs-editor");
        Console.WriteLine($"     docs-editor permissions (now) : [{string.Join(", ", editorNow.OrderBy(p => p))}]");
        Console.WriteLine("       ('docs.publish' is seeded with ValidFrom = now + 1 day → not active yet)");

        // Advance the real clock past the future grant's ValidFrom — the same store now sees it.
        clock.Advance(TimeSpan.FromDays(2));
        var editorLater = await store.GetPermissionsForRoleAsync("docs-editor");
        Console.WriteLine($"     docs-editor permissions (+2d) : [{string.Join(", ", editorLater.OrderBy(p => p))}]");

        var allRoles = await store.GetAllRolesAsync();
        Console.WriteLine($"     all active roles (+2d)        : [{string.Join(", ", allRoles.OrderBy(r => r))}]");

        // Reset the clock for subsequent scenarios.
        clock.Advance(TimeSpan.FromDays(-2));
        Console.WriteLine();
    }

    // ===== 8.2 EfGroupRoleStore =============================================
    private static async Task ShowGroupRoleStore(SampleIdentityDbContext db, IClock clock)
    {
        Console.WriteLine("  8.2 EfGroupRoleStore — temporal group → roles");
        Console.WriteLine("  -----------------------------------------------");

        var store = new EfGroupRoleStore(db, clock);

        var engRoles = await store.GetRolesForGroupAsync("engineering");
        Console.WriteLine($"     engineering roles (now)       : [{string.Join(", ", engRoles.OrderBy(r => r))}]");

        var allGroups = await store.GetAllGroupsAsync();
        Console.WriteLine($"     all active groups             : [{string.Join(", ", allGroups.OrderBy(g => g))}]");
        Console.WriteLine();
    }

    // ===== Seed =============================================================
    private static async Task SeedTemporalMappings(SampleIdentityDbContext db, DateTimeOffset now)
    {
        db.Set<RolePermission>().AddRange(
            new RolePermission { RoleName = "docs-editor", PermissionName = "docs.read", ValidFrom = now.AddDays(-10) },
            new RolePermission { RoleName = "docs-editor", PermissionName = "docs.write", ValidFrom = now.AddDays(-10) },
            // Future grant — active only from +1 day:
            new RolePermission { RoleName = "docs-editor", PermissionName = "docs.publish", ValidFrom = now.AddDays(1) },
            // Expired grant — no longer active:
            new RolePermission { RoleName = "docs-reader", PermissionName = "docs.read", ValidFrom = now.AddDays(-30), ValidTo = now.AddDays(-1) });

        db.Set<GroupRole>().AddRange(
            new GroupRole { GroupName = "engineering", RoleName = "docs-editor", ValidFrom = now.AddDays(-10) },
            new GroupRole { GroupName = "engineering", RoleName = "ci-operator", ValidFrom = now.AddDays(-10) });

        await db.SaveChangesAsync();
    }

    // ===== Demo-only EF context + entities ==================================

    private static SampleIdentityDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SampleIdentityDbContext>()
            .UseInMemoryDatabase($"identity-sample-{Guid.NewGuid()}")
            .Options;
        return new SampleIdentityDbContext(options);
    }

    /// <summary>
    ///     Minimal DbContext exposing the Identity persistence entity sets. The host would
    ///     instead apply <c>ApplyIdentityConfigurations</c> (relational) or use the package's
    ///     OwnsOne composition; the in-memory provider needs no explicit configuration.
    /// </summary>
    private sealed class SampleIdentityDbContext(DbContextOptions options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ExternalIdentityRecord<Guid>>(e => e.Ignore(r => r.ExternalIdentityKey));
            modelBuilder.Entity<RolePermission>();
            modelBuilder.Entity<GroupRole>();
        }
    }
}
