using System.Text;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Sql;

/// <summary>
///     SQLite SQL generator. SQLite has no <c>ALTER COLUMN</c> and cannot add or drop a foreign
///     key on an existing table, so those changes are applied by rebuilding the table
///     (rename → recreate with the desired shape → copy → drop). The runner drives the rebuild
///     via <see cref="GetRebuildTableName" /> / <see cref="GenerateTableRebuild" />; everything
///     else is a direct, idempotent statement.
/// </summary>
public sealed class SqliteMigrationGenerator : SqlGeneratorBase
{
    public override string ProviderName => MigrationConstants.ProviderSqlite;

    protected override string QuoteIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";

    /// <summary>
    ///     Changes SQLite cannot express as a direct statement. Each one is applied by rebuilding
    ///     the whole table with the desired schema.
    ///     <para>
    ///     <see cref="AlterColumnType" /> is included even though SQLite's type affinity makes the
    ///     stored values compatible: the *declared* type is what introspection reads back, so
    ///     leaving it untouched would make the diff re-propose the same change on every run.
    ///     </para>
    /// </summary>
    public override string? GetRebuildTableName(SchemaChange change) => change switch
    {
        AlterColumnNullability c => c.TableName,
        AlterColumnDefault c => c.TableName,
        AlterColumnType c => c.TableName,
        AddForeignKey c => c.TableName,
        DropForeignKey c => c.TableName,
        AlterPrimaryKey c => c.TableName,
        _ => null
    };

    /// <summary>
    ///     Overrides script generation to consolidate table-rebuild changes, so the script the
    ///     runner records (and <c>pragmatic-migrate script</c> prints) matches what is executed.
    /// </summary>
    public override string GenerateScript(SchemaDiff diff)
    {
        // A table created by this same diff already gets its FKs and PK inline in the CREATE, so
        // it is never a rebuild candidate — mirrors what MigrationRunner does when executing.
        var createdTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in diff.Changes)
        {
            if (change is CreateTable createTable)
                createdTables.Add(createTable.Table.Name);
        }

        var rebuildTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in diff.Changes)
        {
            if (GetRebuildTableName(change) is { } tableName && !createdTables.Contains(tableName))
                rebuildTables.Add(tableName);
        }

        // No rebuilds needed — use default behavior, minus the changes the CREATE already covers.
        if (rebuildTables.Count == 0 || diff.DesiredSchema is null)
            return RenderDirectScript(diff, createdTables);

        var sb = new StringBuilder();
        sb.AppendLine($"-- Pragmatic Migrations — {ProviderName}");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"-- Changes: {diff.Changes.Length}");
        sb.AppendLine();

        var rebuiltTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var change in diff.Changes)
        {
            var tableName = SchemaChangeTarget.TableNameOf(change);

            if (tableName is not null && createdTables.Contains(tableName) && GetRebuildTableName(change) is not null)
            {
                sb.AppendLine($"-- {change.Description} — already part of CREATE TABLE \"{tableName}\"");
                sb.AppendLine();
                continue;
            }

            // A rebuild recreates the table with its FULL desired shape, so every other change
            // queued for that table is already satisfied by it — emit the rebuild once and skip
            // the rest, exactly like the runner does.
            if (tableName is not null && rebuildTables.Contains(tableName))
            {
                if (!rebuiltTables.Add(tableName))
                    continue;

                var desiredTable = diff.DesiredSchema.Tables
                    .FirstOrDefault(t => string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase));

                if (desiredTable is null)
                    continue;

                sb.AppendLine($"-- Table rebuild: {tableName}");
                // Script rendering has no live connection to read the current columns from; copy
                // the desired set. The runner passes the real current columns when it executes.
                sb.Append(GenerateTableRebuild(desiredTable, [.. desiredTable.Columns.Select(c => c.Name)]));
                sb.AppendLine();
                continue;
            }

            sb.AppendLine($"-- {change.Description}");
            sb.Append(GenerateChangeScript(change));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Renders the diff without any rebuild, skipping the changes a CREATE TABLE in the same
    ///     diff already expresses inline (SQLite emits FKs and the PK inside the CREATE).
    /// </summary>
    private string RenderDirectScript(SchemaDiff diff, HashSet<string> createdTables)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- Pragmatic Migrations — {ProviderName}");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"-- Changes: {diff.Changes.Length}");
        sb.AppendLine();

        foreach (var change in diff.Changes)
        {
            var tableName = SchemaChangeTarget.TableNameOf(change);
            if (tableName is not null && createdTables.Contains(tableName) && GetRebuildTableName(change) is not null)
            {
                sb.AppendLine($"-- {change.Description} — already part of CREATE TABLE \"{tableName}\"");
                sb.AppendLine();
                continue;
            }

            sb.AppendLine($"-- {change.Description}");
            sb.Append(GenerateChangeScript(change));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    protected override string GenerateCreateTable(CreateTable change) =>
        GenerateCreateTable(change.Table, ifNotExists: true);

    private string GenerateCreateTable(TableSchema t, bool ifNotExists)
    {
        var sb = new StringBuilder();
        var existsClause = ifNotExists ? "IF NOT EXISTS " : "";
        sb.AppendLine($"CREATE TABLE {existsClause}{QuoteIdentifier(t.Name)} (");

        var parts = t.Columns.Select(c => $"    {ColumnDef(c, QuoteIdentifier)}").ToList();

        var pk = t.Columns.Where(c => c.IsPrimaryKey).Select(c => QuoteIdentifier(c.Name)).ToList();
        if (pk.Count > 0)
            parts.Add($"    PRIMARY KEY ({string.Join(", ", pk)})");

        // SQLite FK constraints must be inline at CREATE TABLE
        foreach (var fk in t.ForeignKeys)
        {
            var onDelete = ToSqlDeleteAction(fk.OnDelete);
            parts.Add($"    CONSTRAINT {QuoteIdentifier(fk.Name)} FOREIGN KEY ({QuoteIdentifier(fk.Column)}) " +
                       $"REFERENCES {QuoteIdentifier(fk.ReferencedTable)} ({QuoteIdentifier(fk.ReferencedColumn)}) ON DELETE {onDelete}");
        }

        sb.AppendLine(string.Join(",\n", parts));
        sb.AppendLine(");");
        return sb.ToString();
    }

    protected override string GenerateDropTable(DropTable change) =>
        $"DROP TABLE IF EXISTS {QuoteIdentifier(change.TableName)};\n";

    protected override string GenerateAddColumn(AddColumn change) =>
        $"ALTER TABLE {QuoteIdentifier(change.TableName)} ADD COLUMN {ColumnDef(change.Column, QuoteIdentifier)};\n";

    protected override string GenerateDropColumn(DropColumn change) =>
        $"ALTER TABLE {QuoteIdentifier(change.TableName)} DROP COLUMN {QuoteIdentifier(change.ColumnName)};\n";

    protected override string GenerateRenameColumn(RenameColumn change) =>
        $"ALTER TABLE {QuoteIdentifier(change.TableName)} RENAME COLUMN {QuoteIdentifier(change.OldName)} TO {QuoteIdentifier(change.NewName)};\n";

    protected override string GenerateAlterColumnType(AlterColumnType change) =>
        throw RebuildRequired($"column type change {change.TableName}.{change.ColumnName} ({change.OldType} → {change.NewType})");

    protected override string GenerateAlterColumnNullability(AlterColumnNullability change) =>
        throw RebuildRequired($"nullability change for {change.TableName}.{change.ColumnName}");

    protected override string GenerateAlterColumnDefault(AlterColumnDefault change) =>
        throw RebuildRequired($"default change for {change.TableName}.{change.ColumnName}");

    // Reaching a per-change generator for a rebuild-only change means the caller ignored
    // GetRebuildTableName. Throwing is the only honest outcome: a `SELECT 1/0` meant to abort the
    // migration does not, because SQLite evaluates division by zero to NULL rather than raising —
    // the statement succeeds, the change is silently dropped, and the run reports success on a
    // schema that has not changed.
    private static NotSupportedException RebuildRequired(string what) =>
        new($"SQLite cannot apply {what} as a single statement — it requires a table rebuild. " +
            "Call GetRebuildTableName(change) first and drive GenerateTableRebuild for that table " +
            "(the MigrationRunner and GenerateScript(diff) both do this automatically).");

    protected override string GenerateAddIndex(AddIndex change)
    {
        // SQLite has no concurrent / online index creation — change.IsConcurrent is ignored.
        var idx = change.Index;
        var unique = idx.IsUnique ? "UNIQUE " : "";
        var cols = string.Join(", ", idx.Columns.Select(QuoteIdentifier));
        var filter = idx.Filter is not null ? $" WHERE {idx.Filter}" : "";
        return $"CREATE {unique}INDEX IF NOT EXISTS {QuoteIdentifier(idx.Name)} ON {QuoteIdentifier(change.TableName)} ({cols}){filter};\n";
    }

    protected override string GenerateDropIndex(DropIndex change) =>
        $"DROP INDEX IF EXISTS {QuoteIdentifier(change.IndexName)};\n";

    protected override string GenerateAlterPrimaryKey(AlterPrimaryKey change) =>
        throw RebuildRequired($"the primary key of {change.TableName}");

    protected override string GenerateAddForeignKey(AddForeignKey change) =>
        throw RebuildRequired($"FK {change.ForeignKey.Name} on {change.TableName}");

    protected override string GenerateDropForeignKey(DropForeignKey change) =>
        throw RebuildRequired($"dropping FK {change.ForeignKeyName} on {change.TableName}");

    public override string GenerateAuditTableDdl(string auditTableName = MigrationConstants.AuditTableName)
    {
        var table = QuoteIdentifier(auditTableName);
        var index = QuoteIdentifier($"IX_{auditTableName}_AppliedAt");
        return $"""
            CREATE TABLE IF NOT EXISTS {table} (
                "Id"           INTEGER PRIMARY KEY AUTOINCREMENT,
                "Hash"         TEXT NOT NULL,
                "SchemaJson"   TEXT NOT NULL,
                "AppliedAt"    TEXT NOT NULL DEFAULT (datetime('now')),
                "AppliedBy"    TEXT NULL,
                "DurationMs"   INTEGER NULL,
                "ChangeCount"  INTEGER NULL,
                "SqlScript"    TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS {index} ON {table} ("AppliedAt" DESC);
            """;
    }

    public override string GenerateDataMigrationTableDdl() =>
        """
        CREATE TABLE IF NOT EXISTS "__PragmaticDataMigrations" (
            "Name"       TEXT NOT NULL PRIMARY KEY,
            "AppliedAt"  TEXT NOT NULL DEFAULT (datetime('now')),
            "DurationMs" INTEGER
        );
        """;

    /// <inheritdoc />
    public override string GenerateTableRebuild(TableSchema desiredTable, IReadOnlyCollection<string> currentColumnNames)
    {
        var tableName = QuoteIdentifier(desiredTable.Name);
        var tempName = QuoteIdentifier($"__{desiredTable.Name}_rebuild");

        // Copy only columns that exist on BOTH sides. A column added in the same migration is not
        // in the source table (SELECT would fail), and a column being dropped is not in the target.
        var current = new HashSet<string>(currentColumnNames, StringComparer.OrdinalIgnoreCase);
        var copied = desiredTable.Columns
            .Where(c => current.Contains(c.Name))
            .Select(c => QuoteIdentifier(c.Name))
            .ToList();

        var sb = new StringBuilder();

        // The MigrationRunner owns the surrounding transaction (and SQLite rejects a nested BEGIN),
        // so this script emits NO BEGIN/COMMIT. FK enforcement is suspended with defer_foreign_keys
        // rather than `PRAGMA foreign_keys = OFF`: the latter is a documented no-op inside a
        // transaction, whereas defer_foreign_keys postpones the checks to COMMIT — by which point
        // the table has been recreated and every reference resolves again. It resets itself at the
        // end of the transaction, so nothing has to be restored afterwards.
        sb.AppendLine("PRAGMA defer_foreign_keys = ON;");
        sb.AppendLine($"ALTER TABLE {tableName} RENAME TO {tempName};");

        // Recreate with desired schema (columns + PKs + FKs) — emit a plain CREATE TABLE
        // (no IF NOT EXISTS) so a leftover/conflicting table surfaces as an error inside the
        // transaction rather than being silently skipped.
        sb.Append(GenerateCreateTable(desiredTable, ifNotExists: false));

        // Copy data. With no shared columns there is nothing to preserve — skip the INSERT rather
        // than emitting `INSERT INTO t () SELECT FROM tmp`, which is a syntax error.
        if (copied.Count > 0)
        {
            var columnNames = string.Join(", ", copied);
            sb.AppendLine($"INSERT INTO {tableName} ({columnNames}) SELECT {columnNames} FROM {tempName};");
        }

        sb.AppendLine($"DROP TABLE {tempName};");

        // Recreate indexes — RENAME TO carried the old ones over to the temp table, and DROP TABLE
        // took them with it.
        foreach (var idx in desiredTable.Indexes)
            sb.Append(GenerateAddIndex(new AddIndex(desiredTable.Name, idx)));

        return sb.ToString();
    }

    /// <summary>
    ///     ⚠️ SQLite cannot add or drop a check constraint on an existing table: they live inside the
    ///     CREATE TABLE text and changing them means rebuilding the table. The constraint is emitted with
    ///     the table when it is created; on an existing one this is a comment rather than a silent
    ///     no-op, so a reader of the migration sees what was skipped and why.
    /// </summary>
    protected override string GenerateAddCheckConstraint(AddCheckConstraint change) =>
        $"-- SQLite: check constraint {change.Check.Name} on {change.TableName} requires a table rebuild; "
        + "it is applied when the table is created.\n";

    /// <inheritdoc cref="GenerateAddCheckConstraint" />
    protected override string GenerateDropCheckConstraint(DropCheckConstraint change) =>
        $"-- SQLite: dropping check constraint {change.CheckName} on {change.TableName} requires a table rebuild.\n";
}
