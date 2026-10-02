namespace Pragmatic.Migrations.Runner;

/// <summary>
///     Hook for custom logic before/after individual migration changes.
///     Use for data transformations during schema changes (e.g., converting data types,
///     populating new NOT NULL columns, migrating data between tables).
/// </summary>
public interface IMigrationHook
{
    /// <summary>
    ///     Database name filter. Return null to apply to all databases.
    /// </summary>
    string? DatabaseName { get; }

    /// <summary>
    ///     Called before a schema change is applied. Return false to skip the change.
    /// </summary>
    Task<bool> BeforeChangeAsync(MigrationStepContext context, CancellationToken ct = default)
        => Task.FromResult(true);

    /// <summary>
    ///     Called after a schema change is applied successfully.
    ///     Use for data migration: populate new columns, transform data, etc.
    ///     Create commands via <see cref="MigrationStepContext.Connection" /> and set their
    ///     <c>Transaction</c> to <see cref="MigrationStepContext.Transaction" /> so the data
    ///     migration is atomic with the schema change.
    /// </summary>
    Task AfterChangeAsync(MigrationStepContext context, CancellationToken ct = default)
        => Task.CompletedTask;
}
