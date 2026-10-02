using System.Data.Common;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Introspection;

/// <summary>
///     Reads the current database schema by querying system catalogs.
///     Provider-specific implementations for PostgreSQL, SQL Server, and SQLite.
/// </summary>
public interface ISchemaIntrospector
{
    /// <summary>Database provider name (e.g. "PostgreSql", "SqlServer", "Sqlite").</summary>
    string ProviderName { get; }

    /// <summary>
    ///     Introspects the current database schema from system catalogs.
    ///     Returns a <see cref="SchemaVersion" /> representing the actual state of the database.
    /// </summary>
    Task<SchemaVersion> IntrospectAsync(DbConnection connection, CancellationToken ct = default);

    /// <summary>
    ///     Table names to exclude from introspection (system/audit tables).
    ///     Default: __PragmaticSchema, __EFMigrationsHistory.
    /// </summary>
    IReadOnlyList<string> ExcludedTables { get; }
}
