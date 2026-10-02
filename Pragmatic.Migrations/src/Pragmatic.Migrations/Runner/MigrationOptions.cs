namespace Pragmatic.Migrations.Runner;

/// <summary>
///     Configuration options for a migration run.
/// </summary>
public sealed record MigrationOptions
{
    /// <summary>If true, generates SQL script without executing. Default false.</summary>
    public bool DryRun { get; init; }

    /// <summary>If true, applies breaking changes without confirmation. Default false.</summary>
    public bool Force { get; init; }

    /// <summary>
    ///     If true, <c>AddIndex</c> changes are executed AFTER the schema transaction commits,
    ///     out-of-transaction, so PostgreSQL emits <c>CREATE INDEX CONCURRENTLY</c> and SQL Server
    ///     emits <c>WITH (ONLINE = ON)</c>. SQLite ignores the flag (always inline). A concurrent
    ///     index that fails to build is reported as a failed <see cref="MigrationResult" />; the
    ///     schema change itself is already committed and the failed index is left INVALID on the
    ///     server — drop and recreate it manually. Default: false.
    /// </summary>
    public bool ConcurrentIndexes { get; init; }

    /// <summary>
    ///     Command timeout applied to each statement the migration executes, and the lease length of
    ///     the leader-election lock. It bounds a single statement, not the run as a whole: a
    ///     migration of twenty changes may take up to twenty times this long. Default 30 minutes.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    ///     Timeout for acquiring the distributed migration advisory lock (SQL Server
    ///     <c>sp_getapplock</c>). If the lock cannot be acquired within this window the
    ///     acquisition fails fast rather than blocking the whole migration. PostgreSQL and
    ///     SQLite ignore this value (PostgreSQL uses a blocking session lock, SQLite is
    ///     single-writer). Default 60 seconds.
    /// </summary>
    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     Base polling interval for database leader election while a follower waits for the
    ///     leader to finish. A small randomized jitter is added on top to avoid a thundering
    ///     herd. Default 2 seconds (provider-agnostic <see cref="DatabaseLeaderElection"/>);
    ///     <see cref="PgLeaderElection"/> uses its own shorter advisory-lock poll.
    /// </summary>
    public TimeSpan LeaderPollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Optional filter: only migrate databases whose <c>SchemaVersion.DatabaseName</c> is in this set.
    ///     Null or empty means migrate all databases (default).
    /// </summary>
    public HashSet<string>? DatabaseFilter { get; init; }

    /// <summary>
    ///     Custom audit table name. Default: <see cref="MigrationConstants.AuditTableName"/> ("__PragmaticSchema").
    /// </summary>
    public string AuditTableName { get; init; } = MigrationConstants.AuditTableName;

    /// <summary>
    ///     When true, tables present in the database but absent from the desired schema are left
    ///     alone instead of being dropped. Set by <c>MigrationsBuilder.ManageDeclaredTablesOnly()</c>
    ///     for databases shared with another system. Default: false (unknown tables are dropped,
    ///     which is a breaking change and therefore blocked without Force).
    /// </summary>
    public bool ManageDeclaredTablesOnly { get; init; }

    /// <summary>
    ///     Returns true if the given database name passes the filter (or no filter is set).
    /// </summary>
    public bool ShouldMigrate(string? databaseName)
    {
        if (DatabaseFilter is null or { Count: 0 })
            return true;
        return databaseName is not null && DatabaseFilter.Contains(databaseName);
    }
}
