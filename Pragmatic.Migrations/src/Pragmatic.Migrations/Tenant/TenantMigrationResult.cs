using Pragmatic.Migrations.Runner;

namespace Pragmatic.Migrations.Tenant;

/// <summary>
///     Migration result for a single tenant.
/// </summary>
/// <param name="TenantId">The tenant whose database was migrated.</param>
/// <param name="Result">What the runner did to it.</param>
public sealed record TenantMigrationResult(
    string TenantId,
    MigrationResult Result);
