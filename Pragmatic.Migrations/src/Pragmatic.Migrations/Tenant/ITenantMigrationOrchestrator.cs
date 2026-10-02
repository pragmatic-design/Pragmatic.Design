namespace Pragmatic.Migrations.Tenant;

/// <summary>
///     Orchestrates database migrations across all tenants with dedicated databases.
///     Iterates tenant list, applies schema diff to each, reports progress.
/// </summary>
public interface ITenantMigrationOrchestrator
{
    /// <summary>
    ///     Migrates all active tenants that have dedicated databases.
    /// </summary>
    Task<TenantMigrationSummary> MigrateAllTenantsAsync(
        Schema.SchemaVersion desiredSchema,
        Runner.MigrationOptions options,
        CancellationToken ct = default);

    /// <summary>
    ///     Migrates a specific tenant's database.
    /// </summary>
    Task<Runner.MigrationResult> MigrateTenantAsync(
        string tenantId,
        Schema.SchemaVersion desiredSchema,
        Runner.MigrationOptions options,
        CancellationToken ct = default);
}
