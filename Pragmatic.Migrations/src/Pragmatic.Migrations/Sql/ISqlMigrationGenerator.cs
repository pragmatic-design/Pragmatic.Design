using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Sql;

/// <summary>
///     Generates idempotent SQL migration scripts from a schema diff.
/// </summary>
public interface ISqlMigrationGenerator
{
    /// <summary>Database provider name (e.g. "PostgreSql", "SqlServer", "Sqlite").</summary>
    string ProviderName { get; }

    /// <summary>Generates a complete idempotent SQL script for all changes in the diff.</summary>
    string GenerateScript(SchemaDiff diff);

    /// <summary>Generates idempotent SQL for a single schema change.</summary>
    string GenerateChangeScript(SchemaChange change);

    /// <summary>Generates the DDL for the audit table under the supplied name.</summary>
    /// <param name="auditTableName">
    ///     Table name to create. Must match the name the runner writes to
    ///     (<c>MigrationOptions.AuditTableName</c>), otherwise the audit INSERT targets a table
    ///     that was never created.
    /// </param>
    string GenerateAuditTableDdl(string auditTableName = MigrationConstants.AuditTableName);

    /// <summary>Generates the DDL for the __PragmaticDataMigrations tracking table.</summary>
    string GenerateDataMigrationTableDdl();

    /// <summary>
    ///     Returns the table that must be rebuilt to apply <paramref name="change" />, or null when
    ///     the change can be applied directly with <see cref="GenerateChangeScript" />.
    ///     <para>
    ///     SQLite has no <c>ALTER COLUMN</c> and cannot add or drop a foreign key on an existing
    ///     table: those changes are only expressible as a full table rebuild, which needs the
    ///     desired <see cref="TableSchema" /> that a single change does not carry. The runner asks
    ///     this first and, when it gets a table name, drives
    ///     <see cref="GenerateTableRebuild" /> once for that table instead.
    ///     </para>
    /// </summary>
    string? GetRebuildTableName(SchemaChange change);

    /// <summary>
    ///     Generates the statements that rebuild <paramref name="desiredTable" /> in place,
    ///     preserving the data of the columns that already exist.
    /// </summary>
    /// <param name="desiredTable">The target shape of the table (columns, PK, FKs, indexes).</param>
    /// <param name="currentColumnNames">
    ///     Columns present in the live table. Only their intersection with the desired columns is
    ///     copied — a column being added in the same migration does not exist in the source table
    ///     and must not appear in the copy list.
    /// </param>
    /// <exception cref="NotSupportedException">The provider does not need (or support) table rebuilds.</exception>
    string GenerateTableRebuild(TableSchema desiredTable, IReadOnlyCollection<string> currentColumnNames);
}
