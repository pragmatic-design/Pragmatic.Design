namespace Pragmatic.Migrations;

/// <summary>
///     Well-known constants used throughout the migration system.
/// </summary>
public static class MigrationConstants
{
    /// <summary>Audit table name for tracking migration history.</summary>
    public const string AuditTableName = "__PragmaticSchema";

    /// <summary>Tracking table for applied data migrations (idempotency by name).</summary>
    public const string DataMigrationTableName = "__PragmaticDataMigrations";

    /// <summary>Leader-election lock table created by <see cref="Pragmatic.Migrations.Runner.DatabaseLeaderElection"/>; excluded from introspection.</summary>
    public const string LockTableName = "__PragmaticLock";

    /// <summary>EF Core migrations history table (excluded from introspection).</summary>
    public const string EfMigrationsTableName = "__EFMigrationsHistory";

    /// <summary>
    ///     Prefix marking a table as framework bookkeeping rather than application schema.
    /// </summary>
    /// <remarks>
    ///     Used by the diff engine to refuse to drop such a table when the desired schema does not
    ///     mention it. The audit trail, the outbox, saga state and subject keys all live under this
    ///     prefix, and each belongs to a package that manages its own shape — a differ generated from
    ///     the application's entities cannot know about them, and dropping what it does not recognise
    ///     is destructive in exactly the case it is least equipped to judge.
    /// </remarks>
    public const string FrameworkTablePrefix = "__";

    /// <summary>
    ///     Framework tables that predate the <see cref="FrameworkTablePrefix" /> convention and are
    ///     therefore not protected by it.
    /// </summary>
    /// <remarks>
    ///     The configuration store names its tables <c>pragmatic_*</c>. An application that keeps it in
    ///     the same database as its own entities would otherwise have them dropped as unknown — and one
    ///     of them holds encrypted secrets, which nothing else can reconstruct. Listed rather than
    ///     renamed: renaming would break every existing deployment for a cosmetic gain.
    /// </remarks>
    public static readonly string[] LegacyFrameworkTables =
    [
        "pragmatic_config",
        "pragmatic_config_change",
        "pragmatic_config_notify",
        "pragmatic_secrets",
        "pragmatic_config_audit",
    ];

    /// <summary>Default PostgreSQL schema.</summary>
    public const string PostgreSqlDefaultSchema = "public";

    /// <summary>Default SQL Server schema.</summary>
    public const string SqlServerDefaultSchema = "dbo";

    /// <summary>Provider name: PostgreSQL.</summary>
    public const string ProviderPostgreSql = "PostgreSql";

    /// <summary>Provider name: SQL Server.</summary>
    public const string ProviderSqlServer = "SqlServer";

    /// <summary>Provider name: SQLite.</summary>
    public const string ProviderSqlite = "Sqlite";
}
