using System.Text;
using Pragmatic.Migrations.Diff.Changes;

namespace Pragmatic.Migrations.Sql;

/// <summary>
///     MySQL SQL generator (8.0.16 or later). Identifiers in backticks; the tables are InnoDB, MySQL's default.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ MySQL commits a DDL statement on its own: a migration that fails halfway keeps the changes before the failure,
///         and the runner's transaction cannot take them back. Each change is a single statement, so the step that failed is
///         the one reported.
///     </para>
///     <para>
///         The statements of this library outside the generator (the audit and data-migration tables, a check's expression)
///         quote identifiers with double quotes, which MySQL reads as identifiers only under <c>ANSI_QUOTES</c>:
///         <see cref="SessionSetup" /> is run on every connection the runner opens.
///     </para>
///     <para>
///         MySQL has no partial index: a filtered <see cref="Schema.IndexSchema" /> is refused rather than created without
///         its filter, which would make a unique index stricter than declared.
///     </para>
/// </remarks>
public sealed class MySqlMigrationGenerator : SqlGeneratorBase
{
    /// <summary>The statement run on each connection once it is open: ANSI_QUOTES added to the session's sql_mode.</summary>
    public const string SessionSetup = "SET SESSION sql_mode = CONCAT(@@SESSION.sql_mode, ',ANSI_QUOTES')";

    public override string ProviderName => MigrationConstants.ProviderMySql;

    /// <summary>MySQL refuses a longer name ("Identifier name ... is too long").</summary>
    public override int? MaxIdentifierLength => 64;

    protected override string QuoteIdentifier(string name) => $"`{name.Replace("`", "``")}`";

    private string Table(string name) => QuoteIdentifier(name);

    protected override string GenerateCreateTable(CreateTable change)
    {
        var t = change.Table;
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE IF NOT EXISTS {Table(t.Name)} (");

        var parts = t.Columns.Select(c => $"    {ColumnDef(c, QuoteIdentifier)}").ToList();
        var pk = t.Columns.Where(c => c.IsPrimaryKey).Select(c => QuoteIdentifier(c.Name)).ToList();
        if (pk.Count > 0)
            parts.Add($"    PRIMARY KEY ({string.Join(", ", pk)})"); // MySQL names every primary key PRIMARY

        sb.AppendLine(string.Join(",\n", parts));
        sb.AppendLine(");");
        return sb.ToString();
    }

    protected override string GenerateDropTable(DropTable change) =>
        $"DROP TABLE IF EXISTS {Table(change.TableName)};\n";

    protected override string GenerateAddColumn(AddColumn change) =>
        $"ALTER TABLE {Table(change.TableName)} ADD COLUMN {ColumnDef(change.Column, QuoteIdentifier)};\n";

    protected override string GenerateDropColumn(DropColumn change) =>
        $"ALTER TABLE {Table(change.TableName)} DROP COLUMN {QuoteIdentifier(change.ColumnName)};\n";

    protected override string GenerateRenameColumn(RenameColumn change) =>
        $"ALTER TABLE {Table(change.TableName)} RENAME COLUMN {QuoteIdentifier(change.OldName)} TO {QuoteIdentifier(change.NewName)};\n";

    /// <remarks>
    ///     ⚠️ MODIFY restates the whole column: a default the column had is not carried (the change does not know it), and the
    ///     next diff restores it with an <see cref="AlterColumnDefault" />.
    /// </remarks>
    protected override string GenerateAlterColumnType(AlterColumnType change)
    {
        // MODIFY without NULL/NOT NULL makes the column nullable: an unknown nullability is refused, not guessed.
        var nullable = change.IsNullable ?? throw new NotSupportedException(
            $"MySQL restates the whole column to change its type: {change.TableName}.{change.ColumnName} needs its nullability.");
        return $"ALTER TABLE {Table(change.TableName)} MODIFY COLUMN {QuoteIdentifier(change.ColumnName)} {change.NewType} {(nullable ? "NULL" : "NOT NULL")};\n";
    }

    /// <inheritdoc cref="GenerateAlterColumnType" />
    protected override string GenerateAlterColumnNullability(AlterColumnNullability change)
    {
        if (change.ColumnType.Length == 0)
            throw new NotSupportedException(
                $"MySQL restates the whole column to change its nullability: {change.TableName}.{change.ColumnName} needs its type.");
        return $"ALTER TABLE {Table(change.TableName)} MODIFY COLUMN {QuoteIdentifier(change.ColumnName)} {change.ColumnType} {(change.NewIsNullable ? "NULL" : "NOT NULL")};\n";
    }

    protected override string GenerateAlterColumnDefault(AlterColumnDefault change) =>
        change.NewDefault is not null
            ? $"ALTER TABLE {Table(change.TableName)} ALTER COLUMN {QuoteIdentifier(change.ColumnName)} SET DEFAULT {change.NewDefault};\n"
            : $"ALTER TABLE {Table(change.TableName)} ALTER COLUMN {QuoteIdentifier(change.ColumnName)} DROP DEFAULT;\n";

    protected override string GenerateAddIndex(AddIndex change)
    {
        // MySQL builds an index online by default — change.IsConcurrent asks for nothing more.
        var idx = change.Index;
        if (idx.Filter is not null)
            throw new NotSupportedException(
                $"MySQL has no partial index: {idx.Name} on {change.TableName} declares the filter '{idx.Filter}', which cannot be created as declared.");
        var unique = idx.IsUnique ? "UNIQUE " : "";
        var cols = string.Join(", ", idx.Columns.Select(QuoteIdentifier));
        return $"CREATE {unique}INDEX {QuoteIdentifier(idx.Name)} ON {Table(change.TableName)} ({cols});\n";
    }

    protected override string GenerateDropIndex(DropIndex change) =>
        $"DROP INDEX {QuoteIdentifier(change.IndexName)} ON {Table(change.TableName)};\n";

    protected override string GenerateAddForeignKey(AddForeignKey change)
    {
        var fk = change.ForeignKey;
        return $"ALTER TABLE {Table(change.TableName)} ADD CONSTRAINT {QuoteIdentifier(fk.Name)} " +
               $"FOREIGN KEY ({QuoteIdentifier(fk.Column)}) REFERENCES {Table(fk.ReferencedTable)} ({QuoteIdentifier(fk.ReferencedColumn)}) " +
               $"ON DELETE {ToSqlDeleteAction(fk.OnDelete)};\n";
    }

    protected override string GenerateDropForeignKey(DropForeignKey change) =>
        $"ALTER TABLE {Table(change.TableName)} DROP FOREIGN KEY {QuoteIdentifier(change.ForeignKeyName)};\n";

    protected override string GenerateAlterPrimaryKey(AlterPrimaryKey change)
    {
        var parts = new List<string>();
        if (change.OldColumns.Length > 0)
            parts.Add("DROP PRIMARY KEY");
        if (change.NewColumns.Length > 0)
            parts.Add($"ADD PRIMARY KEY ({string.Join(", ", change.NewColumns.Select(QuoteIdentifier))})");
        return parts.Count == 0 ? "" : $"ALTER TABLE {Table(change.TableName)} {string.Join(", ", parts)};\n";
    }

    // The expression is the schema's, its identifiers in double quotes: ANSI_QUOTES (SessionSetup) reads them.
    protected override string GenerateAddCheckConstraint(AddCheckConstraint change) =>
        $"ALTER TABLE {Table(change.TableName)} ADD CONSTRAINT {QuoteIdentifier(change.Check.Name)} CHECK ({change.Check.Expression});\n";

    protected override string GenerateDropCheckConstraint(DropCheckConstraint change) =>
        $"ALTER TABLE {Table(change.TableName)} DROP CHECK {QuoteIdentifier(change.CheckName)};\n";

    // The index inline: MySQL has no CREATE INDEX IF NOT EXISTS.
    public override string GenerateAuditTableDdl(string auditTableName = MigrationConstants.AuditTableName) => $"""
        CREATE TABLE IF NOT EXISTS {Table(auditTableName)} (
            `Id`           INT AUTO_INCREMENT PRIMARY KEY,
            `Hash`         VARCHAR(64) NOT NULL,
            `SchemaJson`   LONGTEXT NOT NULL,
            `AppliedAt`    DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
            `AppliedBy`    VARCHAR(256) NULL,
            `DurationMs`   BIGINT NULL,
            `ChangeCount`  INT NULL,
            `SqlScript`    LONGTEXT NULL,
            INDEX {QuoteIdentifier($"IX_{auditTableName}_AppliedAt")} (`AppliedAt` DESC)
        );
        """;

    public override string GenerateDataMigrationTableDdl() => """
        CREATE TABLE IF NOT EXISTS `__PragmaticDataMigrations` (
            `Name`       VARCHAR(256) NOT NULL PRIMARY KEY,
            `AppliedAt`  DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
            `DurationMs` BIGINT NULL
        );
        """;
}
