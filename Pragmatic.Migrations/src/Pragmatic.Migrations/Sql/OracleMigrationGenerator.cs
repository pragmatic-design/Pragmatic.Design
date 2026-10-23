using System.Text;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Sql;

/// <summary>
///     Oracle SQL generator (23ai or later: <c>IF [NOT] EXISTS</c> and <c>BOOLEAN</c> are 23ai's). Quoted identifiers, so a
///     name keeps its case; the tables are the connected user's.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Oracle's driver runs one statement per command and refuses a terminating semicolon on a SQL statement, so every
///         change is a single statement without one; where two are needed (the audit table and its index, a primary key
///         replaced) they are a PL/SQL block. And Oracle commits a DDL statement on its own, as MySQL does: a migration that
///         fails halfway keeps the changes before the failure.
///     </para>
///     <para>
///         ⚠️ Oracle stores the empty string as NULL: a <c>NOT NULL</c> text column refuses <c>""</c> (ORA-01400). The
///         schema says what it says; an application writing empty texts there declares the column nullable.
///     </para>
///     <para>
///         Oracle has no partial index: a filtered <see cref="IndexSchema" /> is refused rather than created without its
///         filter.
///     </para>
/// </remarks>
public sealed class OracleMigrationGenerator : SqlGeneratorBase
{
    public override string ProviderName => MigrationConstants.ProviderOracle;

    protected override string QuoteIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";

    private string Table(string name) => QuoteIdentifier(name);

    /// <summary>A column as Oracle writes it: the default before the nullability, which is the order its grammar takes.</summary>
    private string Column(ColumnSchema col)
        => $"{QuoteIdentifier(col.Name)} {col.SqlType}{(col.DefaultValue is null ? "" : " DEFAULT " + col.DefaultValue)} {(col.IsNullable ? "NULL" : "NOT NULL")}";

    protected override string GenerateCreateTable(CreateTable change)
    {
        var t = change.Table;
        var parts = t.Columns.Select(c => $"    {Column(c)}").ToList();
        var pk = t.Columns.Where(c => c.IsPrimaryKey).Select(c => QuoteIdentifier(c.Name)).ToList();
        if (pk.Count > 0)
            parts.Add($"    CONSTRAINT {QuoteIdentifier($"PK_{t.Name}")} PRIMARY KEY ({string.Join(", ", pk)})");

        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE IF NOT EXISTS {Table(t.Name)} (");
        sb.AppendLine(string.Join(",\n", parts));
        sb.Append(')');
        return sb.ToString();
    }

    protected override string GenerateDropTable(DropTable change) =>
        $"DROP TABLE IF EXISTS {Table(change.TableName)} PURGE";

    protected override string GenerateAddColumn(AddColumn change) =>
        $"ALTER TABLE {Table(change.TableName)} ADD ({Column(change.Column)})";

    protected override string GenerateDropColumn(DropColumn change) =>
        $"ALTER TABLE {Table(change.TableName)} DROP COLUMN {QuoteIdentifier(change.ColumnName)}";

    protected override string GenerateRenameColumn(RenameColumn change) =>
        $"ALTER TABLE {Table(change.TableName)} RENAME COLUMN {QuoteIdentifier(change.OldName)} TO {QuoteIdentifier(change.NewName)}";

    // MODIFY changes only what it names: the column keeps its default and its nullability.
    protected override string GenerateAlterColumnType(AlterColumnType change) =>
        $"ALTER TABLE {Table(change.TableName)} MODIFY ({QuoteIdentifier(change.ColumnName)} {change.NewType})";

    protected override string GenerateAlterColumnNullability(AlterColumnNullability change) =>
        $"ALTER TABLE {Table(change.TableName)} MODIFY ({QuoteIdentifier(change.ColumnName)} {(change.NewIsNullable ? "NULL" : "NOT NULL")})";

    // Oracle cannot remove a default, only set it to NULL; the introspector reads a NULL default as none.
    protected override string GenerateAlterColumnDefault(AlterColumnDefault change) =>
        $"ALTER TABLE {Table(change.TableName)} MODIFY ({QuoteIdentifier(change.ColumnName)} DEFAULT {change.NewDefault ?? "NULL"})";

    protected override string GenerateAddIndex(AddIndex change)
    {
        // An ONLINE build is an Enterprise Edition option: change.IsConcurrent asks for nothing here.
        var idx = change.Index;
        if (idx.Filter is not null)
            throw new NotSupportedException(
                $"Oracle has no partial index: {idx.Name} on {change.TableName} declares the filter '{idx.Filter}', which cannot be created as declared.");
        var unique = idx.IsUnique ? "UNIQUE " : "";
        var cols = string.Join(", ", idx.Columns.Select(QuoteIdentifier));
        return $"CREATE {unique}INDEX IF NOT EXISTS {QuoteIdentifier(idx.Name)} ON {Table(change.TableName)} ({cols})";
    }

    protected override string GenerateDropIndex(DropIndex change) =>
        $"DROP INDEX IF EXISTS {QuoteIdentifier(change.IndexName)}";

    protected override string GenerateAddForeignKey(AddForeignKey change)
    {
        var fk = change.ForeignKey;
        return $"ALTER TABLE {Table(change.TableName)} ADD CONSTRAINT {QuoteIdentifier(fk.Name)} " +
               $"FOREIGN KEY ({QuoteIdentifier(fk.Column)}) REFERENCES {Table(fk.ReferencedTable)} ({QuoteIdentifier(fk.ReferencedColumn)})" +
               OnDelete(fk.OnDelete);
    }

    /// <summary>Oracle writes CASCADE and SET NULL; NO ACTION is what it does without the clause, and it has no RESTRICT.</summary>
    private static string OnDelete(ReferentialAction action) => action switch
    {
        ReferentialAction.Cascade => " ON DELETE CASCADE",
        ReferentialAction.SetNull => " ON DELETE SET NULL",
        _ => ""
    };

    protected override string GenerateDropForeignKey(DropForeignKey change) =>
        $"ALTER TABLE {Table(change.TableName)} DROP CONSTRAINT {QuoteIdentifier(change.ForeignKeyName)}";

    protected override string GenerateAlterPrimaryKey(AlterPrimaryKey change)
    {
        // The existing key's name is whatever created it, so it is looked up rather than assumed.
        var table = Literal(Table(change.TableName));
        var sb = new StringBuilder();
        sb.AppendLine("BEGIN");
        sb.AppendLine($"    FOR k IN (SELECT constraint_name FROM user_constraints WHERE table_name = '{change.TableName.Replace("'", "''")}' AND constraint_type = 'P') LOOP");
        sb.AppendLine($"        EXECUTE IMMEDIATE 'ALTER TABLE {table} DROP CONSTRAINT \"' || k.constraint_name || '\"';");
        sb.AppendLine("    END LOOP;");
        if (change.NewColumns.Length > 0)
        {
            var cols = Literal(string.Join(", ", change.NewColumns.Select(QuoteIdentifier)));
            sb.AppendLine($"    EXECUTE IMMEDIATE 'ALTER TABLE {table} ADD CONSTRAINT {Literal(QuoteIdentifier($"PK_{change.TableName}"))} PRIMARY KEY ({cols})';");
        }

        sb.Append("END;");
        return sb.ToString();
    }

    protected override string GenerateAddCheckConstraint(AddCheckConstraint change) =>
        $"ALTER TABLE {Table(change.TableName)} ADD CONSTRAINT {QuoteIdentifier(change.Check.Name)} CHECK ({change.Check.Expression})";

    protected override string GenerateDropCheckConstraint(DropCheckConstraint change) =>
        $"ALTER TABLE {Table(change.TableName)} DROP CONSTRAINT {QuoteIdentifier(change.CheckName)}";

    /// <remarks>
    ///     The key is a SYS_GUID() default, not an identity column: an identity creates a sequence, which an application user
    ///     holding CREATE TABLE alone may not (ORA-01031); the history is read by AppliedAt, never by the key.
    /// </remarks>
    public override string GenerateAuditTableDdl(string auditTableName = MigrationConstants.AuditTableName)
    {
        var table = Table(auditTableName);
        var create = $"""
            CREATE TABLE IF NOT EXISTS {table} (
                "Id"           RAW(16) DEFAULT SYS_GUID() NOT NULL PRIMARY KEY,
                "Hash"         VARCHAR2(64) NOT NULL,
                "SchemaJson"   CLOB NOT NULL,
                "AppliedAt"    TIMESTAMP DEFAULT SYS_EXTRACT_UTC(SYSTIMESTAMP) NOT NULL,
                "AppliedBy"    VARCHAR2(256) NULL,
                "DurationMs"   NUMBER(19) NULL,
                "ChangeCount"  NUMBER(10) NULL,
                "SqlScript"    CLOB NULL
            )
            """;
        var index = $"CREATE INDEX IF NOT EXISTS {QuoteIdentifier($"IX_{auditTableName}_AppliedAt")} ON {table} (\"AppliedAt\" DESC)";
        return $"BEGIN\n    EXECUTE IMMEDIATE '{Literal(create)}';\n    EXECUTE IMMEDIATE '{Literal(index)}';\nEND;";
    }

    public override string GenerateDataMigrationTableDdl() => """
        CREATE TABLE IF NOT EXISTS "__PragmaticDataMigrations" (
            "Name"       VARCHAR2(256) NOT NULL PRIMARY KEY,
            "AppliedAt"  TIMESTAMP DEFAULT SYS_EXTRACT_UTC(SYSTIMESTAMP) NOT NULL,
            "DurationMs" NUMBER(19) NULL
        )
        """;

    /// <summary>Text as a PL/SQL string literal's payload.</summary>
    private static string Literal(string text) => text.Replace("'", "''");
}
