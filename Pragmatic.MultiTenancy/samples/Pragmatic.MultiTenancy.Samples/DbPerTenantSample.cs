using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.MultiTenancy.Persistence;

namespace Pragmatic.MultiTenancy.Samples;

/// <summary>
///     Demonstrates the DB-per-tenant Persistence package: <see cref="TenantDatabaseOptions"/>
///     (safe connection-string templating), <see cref="TenantConnectionStringProvider{TDbContext}"/>
///     (per-tenant connection resolution + caching, exercised fully in-memory against an
///     <see cref="InMemoryTenantStore"/>), and the <see cref="DbPerTenantServiceExtensions"/>
///     DI wiring including provisioner selection.
/// </summary>
/// <remarks>
///     The concrete <c>PostgresTenantProvisioner</c> / <c>SqlServerTenantProvisioner</c> issue real
///     <c>CREATE DATABASE</c> DDL and therefore require a live server; they are shown as compiling
///     setup-only wiring with comments rather than executed here.
/// </remarks>
public static class DbPerTenantSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("10. DB-per-Tenant (Persistence) — connection resolution (executed)");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // 10.1 Safe connection-string templating.
        Console.WriteLine("  10.1 TenantDatabaseOptions.BuildConnectionString — validated templating");
        Console.WriteLine("  ----------------------------------------------------------------------");
        var options = new TenantDatabaseOptions
        {
            DefaultConnectionString = "Server=shared;Database=app",
            ConnectionStringTemplate = "Server=localhost;Database=tenant_{0}"
        };
        Console.WriteLine($"    BuildConnectionString(\"acme\")   → \"{options.BuildConnectionString("acme")}\"");
        try
        {
            options.BuildConnectionString("acme; DROP TABLE users--");
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"    Unsafe id rejected              → {ex.GetType().Name} (injection blocked)");
        }
        Console.WriteLine();

        // 10.2 Per-tenant connection resolution via TenantConnectionStringProvider.
        Console.WriteLine("  10.2 TenantConnectionStringProvider — dedicated vs shared connection");
        Console.WriteLine("  --------------------------------------------------------------------");
        var store = new InMemoryTenantStore();
        store.Seed(
        [
            new TenantInfo { TenantId = "acme", TenantName = "Acme", ConnectionString = "Server=db-acme;Database=acme", State = TenantState.Active, CreatedAt = DateTimeOffset.UtcNow },
            new TenantInfo { TenantId = "globex", TenantName = "Globex", State = TenantState.Active, CreatedAt = DateTimeOffset.UtcNow } // no dedicated CS → falls back to default
        ]);

        // Resolve acme (dedicated database).
        var acmeContext = new MutableTenantContext { TenantId = "acme" };
        var acmeProvider = new TenantConnectionStringProvider<SampleDbContext>(acmeContext, store, options);
        Console.WriteLine($"    acme   (dedicated CS) → \"{await acmeProvider.GetConnectionStringAsync()}\"");

        // Resolve globex (no dedicated CS → default / shared).
        var globexContext = new MutableTenantContext { TenantId = "globex" };
        var globexProvider = new TenantConnectionStringProvider<SampleDbContext>(globexContext, store, options);
        Console.WriteLine($"    globex (shared CS)    → \"{await globexProvider.GetConnectionStringAsync()}\"");

        // Second resolve for acme hits the in-memory cache (same value, no store round-trip).
        Console.WriteLine($"    acme   (cached)       → \"{await acmeProvider.GetConnectionStringAsync()}\"");
        acmeProvider.InvalidateCache("acme");
        Console.WriteLine("    InvalidateCache(\"acme\") called → next resolve re-reads the store");
        Console.WriteLine();

        // 10.3 Deactivated tenant resolution fails fast.
        Console.WriteLine("  10.3 Deactivated tenant → resolution throws");
        Console.WriteLine("  -------------------------------------------");
        await store.DeactivateAsync("acme");
        var freshProvider = new TenantConnectionStringProvider<SampleDbContext>(
            new MutableTenantContext { TenantId = "acme" }, store, options);
        try
        {
            await freshProvider.GetConnectionStringAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Deactivated acme → {ex.GetType().Name}");
        }
        Console.WriteLine();

        // 10.4 DI wiring — AddDbPerTenant + provisioner selection.
        Console.WriteLine("  10.4 DI wiring — AddDbPerTenant + provisioner selection");
        Console.WriteLine("  ------------------------------------------------------");
        using var provider = new ServiceCollection()
            .AddDbPerTenant(o =>
            {
                o.DefaultConnectionString = "Server=shared;Database=app";
                o.ConnectionStringTemplate = "Server=localhost;Database=tenant_{0}";
            })
            .BuildServiceProvider();

        var provisioner = provider.GetRequiredService<ITenantDatabaseProvisioner>();
        Console.WriteLine($"    Default provisioner   : {provisioner.GetType().Name}");
        Console.WriteLine($"    NoOp.ExistsAsync(...) : {await provisioner.ExistsAsync("Server=any")} (assumes external provisioning)");
        Console.WriteLine();

        // 10.5 Auto-provision selection (setup-only — concrete provisioners need a live server).
        Console.WriteLine("  10.5 UseAutoProvision<T>() — opt into real CREATE DATABASE (setup-only)");
        Console.WriteLine("  ---------------------------------------------------------------------");
        Console.WriteLine("""
            // Postgres / SQL Server provisioners issue real DDL and need a live server,
            // so they are wired but not executed in this console sample:
            //
            //   services.AddDbPerTenant(o =>
            //   {
            //       o.DefaultConnectionString = config.GetConnectionString("Default")!;
            //       o.ConnectionStringTemplate = "Host=localhost;Database=tenant_{0};Username=app;Password=...";
            //   });
            //   services.UseAutoProvision<PostgresTenantProvisioner>();   // or SqlServerTenantProvisioner
            //
            //   // Then, when a new tenant first appears — BOTH steps, and the application does them:
            //   var connectionString = options.BuildConnectionString(tenantId); // validated id
            //   await provisioner.ProvisionAsync(tenantId, connectionString, ct); // CREATE DATABASE if absent
            //   await migrations.MigrateAsync(                                    // ...and put a schema in it
            //       new MigrationContext(connectionString, AppDatabaseSchema.Current, new MigrationOptions()), ct);
            //
            // ⚠️ The second line is not optional and nothing does it for you: the provisioner creates an
            // EMPTY database. The schema constant is generated into the host, which is why this
            // composition lives in the application and not behind one framework call.
        """);
        Console.WriteLine();
    }

    /// <summary>Minimal DbContext used only to parameterize the generic connection-string provider.</summary>
    private sealed class SampleDbContext : Microsoft.EntityFrameworkCore.DbContext;
}
