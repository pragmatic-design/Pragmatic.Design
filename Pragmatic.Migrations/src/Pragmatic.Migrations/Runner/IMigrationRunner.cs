namespace Pragmatic.Migrations.Runner;

/// <summary>
///     Orchestrates the migration pipeline: introspect → diff → generate SQL → execute.
/// </summary>
public interface IMigrationRunner
{
    /// <summary>
    ///     Runs a migration against the database described by the context.
    /// </summary>
    Task<MigrationResult> MigrateAsync(MigrationContext context, CancellationToken ct = default);
}
