using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Sql;
using Pragmatic.Migrations.Tenant;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Migrations.Samples.Samples;

/// <summary>
///     Scenario 11 — <see cref="TenantMigrationOrchestrator" /> (DB-per-tenant). Each tenant
///     with a dedicated <c>ConnectionString</c> is migrated to the same desired schema, with
///     per-tenant isolation: one tenant failing does not abort the others. The orchestrator
///     pulls tenants from an <see cref="ITenantStore" /> and runs the shared
///     <see cref="IMigrationRunner" /> against each. Here each tenant is a separate SQLite file.
/// </summary>
public static class TenantMigrationSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Scenario 11: TenantMigrationOrchestrator — DB-per-tenant migration ---");

        var tenants = new List<TenantInfo>
        {
            NewTenant("acme"),
            NewTenant("globex"),
            NewTenant("initech"),
        };

        var store = new InMemoryTenantStore(tenants);
        var runner = BuildRunner();
        var orchestrator = new TenantMigrationOrchestrator(store, runner);

        try
        {
            var summary = await orchestrator.MigrateAllTenantsAsync(DesiredSchemas.V2, new MigrationOptions());

            Console.WriteLine($"  tenants total      : {summary.TotalTenants}");
            Console.WriteLine($"  succeeded / failed / skipped : {summary.SuccessCount} / {summary.FailureCount} / {summary.SkippedCount}");
            foreach (var r in summary.Results)
                Console.WriteLine($"    - {r.TenantId,-8} success={r.Result.Success}, changes={r.Result.ChangesApplied}");
            Console.WriteLine("  verdict            : each dedicated-DB tenant migrated independently; failures are isolated per tenant.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var t in tenants)
            {
                var path = new SqliteConnectionStringBuilder(t.ConnectionString!).DataSource;
                if (File.Exists(path)) File.Delete(path);
            }
        }

        Console.WriteLine();
    }

    private static TenantInfo NewTenant(string id)
    {
        var path = Path.Combine(Path.GetTempPath(), $"prag-tenant-{id}-{Guid.NewGuid():N}.db");
        return new TenantInfo
        {
            TenantId = id,
            TenantName = id,
            ConnectionString = $"Data Source={path}", // non-null → dedicated database (DB-per-tenant)
            State = TenantState.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static MigrationRunner BuildRunner()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISchemaIntrospector>(new SqliteSchemaIntrospector());
        services.AddSingleton<ISqlMigrationGenerator>(new SqliteMigrationGenerator());
        services.AddSingleton<IConnectionFactory>(new SqliteRunnerConnectionFactory());
        var factory = new MigrationProviderFactory(services.BuildServiceProvider());

        return new MigrationRunner(factory, new SchemaDiffEngine(), leaderElection: new AlwaysLeaderElection());
    }

    private sealed class SqliteRunnerConnectionFactory : IConnectionFactory
    {
        public string ProviderName => "Sqlite";

        public async Task<DbConnection> CreateOpenConnectionAsync(string connectionString, CancellationToken ct = default)
        {
            var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync(ct);
            return conn;
        }
    }

    /// <summary>Minimal in-memory tenant store. The orchestrator uses GetActiveAsync + UpdateAsync.</summary>
    private sealed class InMemoryTenantStore(List<TenantInfo> tenants) : ITenantStore
    {
        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default) =>
            Task.FromResult(tenants.FirstOrDefault(t => t.TenantId == tenantId));

        public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantInfo>>(
                tenants.Where(t => t.State == TenantState.Active).ToList());

        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantInfo>>(tenants.ToList());

        public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            tenants.Add(tenant);
            return Task.FromResult(tenant);
        }

        public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            var i = tenants.FindIndex(t => t.TenantId == tenant.TenantId);
            if (i < 0) return Task.FromResult(false);
            tenants[i] = tenant;
            return Task.FromResult(true);
        }

        public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
        {
            var i = tenants.FindIndex(t => t.TenantId == tenantId);
            if (i < 0) return Task.FromResult(false);
            tenants[i] = tenants[i] with { State = TenantState.Suspended };
            return Task.FromResult(true);
        }
    }
}
