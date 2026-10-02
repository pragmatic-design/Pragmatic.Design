using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Diff.Changes;

/// <summary>
///     Add a new index to a table. When <see cref="IsConcurrent"/> is true the runner
///     defers execution to a post-commit phase outside the migration transaction so
///     PostgreSQL can use <c>CREATE INDEX CONCURRENTLY</c> and SQL Server can use
///     <c>WITH (ONLINE = ON)</c> — large tables are not locked during the build.
///     The flag is set by the runner from <c>MigrationOptions.ConcurrentIndexes</c>;
///     the diff engine itself emits <see cref="IsConcurrent"/> = false.
/// </summary>
public sealed record AddIndex(string TableName, IndexSchema Index, bool IsConcurrent = false, string? SchemaName = null)
    : SchemaChange($"ADD INDEX {Index.Name} ON {TableName}", IsBreaking: false);
