using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Migrations.Tenant;

/// <inheritdoc />
public sealed class DatabaseMigrationSweep(
    IMigrationRunner runner,
    ITenantMigrationOrchestrator tenants,
    ITenantStore register) : IDatabaseMigrationSweep
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MigratedDatabase>> RunAsync(
        string sharedConnectionString,
        SchemaVersion desiredSchema,
        MigrationOptions? options = null,
        CancellationToken ct = default)
    {
        var migrationOptions = options ?? new MigrationOptions();
        var report = new List<MigratedDatabase>();

        // The shared database first: it holds the register, so a tenant read below would otherwise be
        // answered by a database that is behind its own schema.
        var shared = await runner
            .MigrateAsync(new MigrationContext(sharedConnectionString, desiredSchema, migrationOptions), ct)
            .ConfigureAwait(false);

        report.Add(new MigratedDatabase(
            DatabaseIn(sharedConnectionString), null, shared.Success, shared.ChangesApplied, shared.Error));

        var sweep = await tenants
            .MigrateAllTenantsAsync(desiredSchema, migrationOptions, ct)
            .ConfigureAwait(false);

        foreach (var tenant in sweep.Results)
        {
            // The connection string comes from the register rather than from the result, because a
            // result carries the tenant and not its database — and the report is about databases.
            var known = await register.GetByIdAsync(tenant.TenantId, ct).ConfigureAwait(false);

            report.Add(new MigratedDatabase(
                DatabaseIn(known?.ConnectionString),
                tenant.TenantId,
                tenant.Result.Success,
                tenant.Result.ChangesApplied,
                tenant.Result.Error));
        }

        // ⚠️ And the databases nothing was done to, which is the half that is easy to leave silent: a tenant
        // the sweep does not visit because it is not active, and the ones a stopped run never reached.
        // They are reported as succeeded-with-no-changes and an explanation, not as failures — nothing
        // went wrong, it just did not happen, and an operator has to be able to tell those apart.
        foreach (var missed in sweep.NotVisited)
            report.Add(new MigratedDatabase(
                DatabaseIn(missed.ConnectionString), missed.TenantId, true, 0,
                $"not visited: {missed.Reason}."));

        return report;
    }

    private static string DatabaseIn(string? connectionString)
        => ConnectionStringInfo.DatabaseIn(connectionString);
}
