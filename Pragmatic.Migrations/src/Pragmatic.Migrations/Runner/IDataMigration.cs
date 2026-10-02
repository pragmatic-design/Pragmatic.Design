using System.Data.Common;

namespace Pragmatic.Migrations.Runner;

/// <summary>
///     A named, versioned data transformation run as part of a migration — backfilling
///     columns, transforming values, moving data between tables. Each data migration runs
///     exactly once per database, tracked by <see cref="Name" /> in the
///     <c>__PragmaticDataMigrations</c> table, inside a dedicated transaction after the
///     schema changes have been applied.
/// </summary>
public interface IDataMigration
{
    /// <summary>
    ///     Stable unique identifier. The data migration runs once per database; renaming it
    ///     causes it to run again. Use a descriptive, immutable name
    ///     (e.g. <c>"2026-05_BackfillOrderStatus"</c>).
    /// </summary>
    string Name { get; }

    /// <summary>Execution order relative to other data migrations (lower runs first). Default: 0.</summary>
    int Order => 0;

    /// <summary>Database name filter. Null applies the migration to every database in the topology.</summary>
    string? DatabaseName => null;

    /// <summary>
    ///     Performs the data transformation. Commands MUST be enrolled in
    ///     <paramref name="transaction" /> (set <c>DbCommand.Transaction</c>) so the change is
    ///     atomic with the tracking record that marks this migration as applied.
    /// </summary>
    Task MigrateAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct = default);
}
