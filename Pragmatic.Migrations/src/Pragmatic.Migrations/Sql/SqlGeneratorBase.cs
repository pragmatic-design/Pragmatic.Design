using System.Text;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Sql;

/// <summary>
///     Base class for provider-specific SQL generators.
///     Provides shared patterns: script header, change dispatch, identifier quoting.
/// </summary>
public abstract class SqlGeneratorBase : ISqlMigrationGenerator
{
    public abstract string ProviderName { get; }

    public virtual string GenerateScript(SchemaDiff diff)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- Pragmatic Migrations — {ProviderName}");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"-- Changes: {diff.Changes.Length}");
        sb.AppendLine();

        foreach (var change in diff.Changes)
        {
            sb.AppendLine($"-- {change.Description}");
            sb.Append(GenerateChange(change));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public abstract string GenerateAuditTableDdl(string auditTableName = MigrationConstants.AuditTableName);

    public abstract string GenerateDataMigrationTableDdl();

    /// <summary>
    ///     Providers that can express every change with a direct statement need no rebuild.
    ///     SQLite overrides this.
    /// </summary>
    public virtual string? GetRebuildTableName(SchemaChange change) => null;

    /// <inheritdoc />
    public virtual string GenerateTableRebuild(TableSchema desiredTable, IReadOnlyCollection<string> currentColumnNames)
        => throw new NotSupportedException(
            $"{ProviderName} applies every schema change directly and does not support table rebuilds.");

    public string GenerateChangeScript(SchemaChange change) => GenerateChange(change);

    private string GenerateChange(SchemaChange change) => change switch
    {
        CreateTable c => GenerateCreateTable(c),
        DropTable c => GenerateDropTable(c),
        AddColumn c => GenerateAddColumn(c),
        DropColumn c => GenerateDropColumn(c),
        RenameColumn c => GenerateRenameColumn(c),
        AlterColumnType c => GenerateAlterColumnType(c),
        AlterColumnNullability c => GenerateAlterColumnNullability(c),
        AlterColumnDefault c => GenerateAlterColumnDefault(c),
        AddIndex c => GenerateAddIndex(c),
        DropIndex c => GenerateDropIndex(c),
        AddForeignKey c => GenerateAddForeignKey(c),
        DropForeignKey c => GenerateDropForeignKey(c),
        AddCheckConstraint c => GenerateAddCheckConstraint(c),
        DropCheckConstraint c => GenerateDropCheckConstraint(c),
        AlterPrimaryKey c => GenerateAlterPrimaryKey(c),
        _ => $"-- UNSUPPORTED: {change.Description}\n"
    };

    protected abstract string GenerateCreateTable(CreateTable change);
    protected abstract string GenerateDropTable(DropTable change);
    protected abstract string GenerateAddColumn(AddColumn change);
    protected abstract string GenerateDropColumn(DropColumn change);
    protected abstract string GenerateRenameColumn(RenameColumn change);
    protected abstract string GenerateAlterColumnType(AlterColumnType change);
    protected abstract string GenerateAlterColumnNullability(AlterColumnNullability change);
    protected abstract string GenerateAlterColumnDefault(AlterColumnDefault change);
    protected abstract string GenerateAddIndex(AddIndex change);
    protected abstract string GenerateDropIndex(DropIndex change);
    protected abstract string GenerateAddForeignKey(AddForeignKey change);
    protected abstract string GenerateDropForeignKey(DropForeignKey change);

    /// <summary>Adds a row-level invariant the database enforces.</summary>
    /// <remarks>
    ///     ⚠️ Unlike a partial unique index, a check is evaluated per row as that row is written, so it
    ///     does not care in which order a unit of work emits its statements. That is exactly why it can
    ///     state "an interval may not end before it starts" while a unique index cannot state
    ///     "one open stretch per parent" without tripping on a close-then-open in one SaveChanges.
    /// </remarks>
    protected abstract string GenerateAddCheckConstraint(AddCheckConstraint change);

    /// <summary>Drops a check constraint.</summary>
    protected abstract string GenerateDropCheckConstraint(DropCheckConstraint change);
    protected abstract string GenerateAlterPrimaryKey(AlterPrimaryKey change);

    protected abstract string QuoteIdentifier(string name);

    protected string QualifiedTableName(string tableName, string? schemaName) =>
        schemaName is not null ? $"{QuoteIdentifier(schemaName)}.{QuoteIdentifier(tableName)}" : QuoteIdentifier(tableName);

    protected static string ColumnDef(ColumnSchema col, Func<string, string> quote)
    {
        var nullable = col.IsNullable ? "NULL" : "NOT NULL";
        var def = col.DefaultValue is not null ? $" DEFAULT {col.DefaultValue}" : "";
        return $"{quote(col.Name)} {col.SqlType} {nullable}{def}";
    }

    /// <summary>Converts a <see cref="ReferentialAction" /> to its SQL ON DELETE keyword.</summary>
    protected virtual string ToSqlDeleteAction(ReferentialAction onDelete) => onDelete switch
    {
        ReferentialAction.NoAction => "NO ACTION",
        ReferentialAction.Cascade => "CASCADE",
        ReferentialAction.SetNull => "SET NULL",
        ReferentialAction.Restrict => "NO ACTION", // RESTRICT is equivalent to NO ACTION; SQL Server doesn't support RESTRICT keyword
        _ => "NO ACTION"
    };
}
