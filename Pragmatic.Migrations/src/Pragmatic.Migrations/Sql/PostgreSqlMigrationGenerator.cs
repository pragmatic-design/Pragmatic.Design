using System.Text;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Sql;

/// <summary>
///     PostgreSQL SQL generator. Uses DO $$ blocks for idempotent DDL.
/// </summary>
public sealed class PostgreSqlMigrationGenerator : SqlGeneratorBase
{
    /// <summary>
    ///     Schema used for tables that do not declare one. Defaults to "public" (the PostgreSQL
    ///     default). Configurable for hosts that place their objects in a non-default schema.
    /// </summary>
    public string DefaultSchema { get; init; } = "public";

    public override string ProviderName => MigrationConstants.ProviderPostgreSql;

    // Double any embedded quote: an identifier carrying one would otherwise close the
    // quoted name early and change the meaning of the statement.
    protected override string QuoteIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";

    private string Table(string name, string? schema = null) => QualifiedTableName(name, schema ?? DefaultSchema);

    /// <summary>Schema name as a SQL string-literal payload for information_schema lookups.</summary>
    private string SchemaLiteral(string? schema) => (schema ?? DefaultSchema).Replace("'", "''");

    protected override string GenerateCreateTable(CreateTable change)
    {
        var t = change.Table;
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE IF NOT EXISTS {Table(t.Name, t.SchemaName ?? DefaultSchema)} (");

        var cols = t.Columns.Select(c => $"    {ColumnDef(c, QuoteIdentifier)}").ToList();

        var pk = t.Columns.Where(c => c.IsPrimaryKey).Select(c => QuoteIdentifier(c.Name)).ToList();
        if (pk.Count > 0)
            cols.Add($"    CONSTRAINT \"PK_{t.Name}\" PRIMARY KEY ({string.Join(", ", pk)})");

        sb.AppendLine(string.Join(",\n", cols));
        sb.AppendLine(");");
        return sb.ToString();
    }

    protected override string GenerateDropTable(DropTable change) =>
        $"DROP TABLE IF EXISTS {Table(change.TableName, change.SchemaName)};\n";

    protected override string GenerateAddColumn(AddColumn change)
    {
        // Escape single quotes in names used inside SQL string literals within the DO block
        var safeTableName = change.TableName.Replace("'", "''");
        var safeColumnName = change.Column.Name.Replace("'", "''");
        return $$"""
        DO $$ BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = '{{SchemaLiteral(change.SchemaName)}}' AND table_name = '{{safeTableName}}' AND column_name = '{{safeColumnName}}'
            ) THEN
                ALTER TABLE {{Table(change.TableName, change.SchemaName)}} ADD COLUMN {{ColumnDef(change.Column, QuoteIdentifier)}};
            END IF;
        END $$;
        """;
    }

    protected override string GenerateDropColumn(DropColumn change) =>
        $"ALTER TABLE {Table(change.TableName, change.SchemaName)} DROP COLUMN IF EXISTS {QuoteIdentifier(change.ColumnName)};\n";

    protected override string GenerateRenameColumn(RenameColumn change)
    {
        // Escape single quotes in names used inside SQL string literals within the DO block
        var safeTableName = change.TableName.Replace("'", "''");
        var safeOldName = change.OldName.Replace("'", "''");
        var safeNewName = change.NewName.Replace("'", "''");
        return $$"""
        DO $$ BEGIN
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = '{{SchemaLiteral(change.SchemaName)}}' AND table_name = '{{safeTableName}}' AND column_name = '{{safeOldName}}'
            ) AND NOT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = '{{SchemaLiteral(change.SchemaName)}}' AND table_name = '{{safeTableName}}' AND column_name = '{{safeNewName}}'
            ) THEN
                ALTER TABLE {{Table(change.TableName, change.SchemaName)}} RENAME COLUMN {{QuoteIdentifier(change.OldName)}} TO {{QuoteIdentifier(change.NewName)}};
            END IF;
        END $$;
        """;
    }

    protected override string GenerateAlterColumnType(AlterColumnType change) =>
        $"ALTER TABLE {Table(change.TableName, change.SchemaName)} ALTER COLUMN {QuoteIdentifier(change.ColumnName)} TYPE {change.NewType} USING {QuoteIdentifier(change.ColumnName)}::{change.NewType};\n";

    protected override string GenerateAlterColumnNullability(AlterColumnNullability change) =>
        change.NewIsNullable
            ? $"ALTER TABLE {Table(change.TableName, change.SchemaName)} ALTER COLUMN {QuoteIdentifier(change.ColumnName)} DROP NOT NULL;\n"
            : $"ALTER TABLE {Table(change.TableName, change.SchemaName)} ALTER COLUMN {QuoteIdentifier(change.ColumnName)} SET NOT NULL;\n";

    protected override string GenerateAlterColumnDefault(AlterColumnDefault change) =>
        change.NewDefault is not null
            ? $"ALTER TABLE {Table(change.TableName, change.SchemaName)} ALTER COLUMN {QuoteIdentifier(change.ColumnName)} SET DEFAULT {change.NewDefault};\n"
            : $"ALTER TABLE {Table(change.TableName, change.SchemaName)} ALTER COLUMN {QuoteIdentifier(change.ColumnName)} DROP DEFAULT;\n";

    protected override string GenerateAddIndex(AddIndex change)
    {
        var idx = change.Index;
        var unique = idx.IsUnique ? "UNIQUE " : "";
        // CONCURRENTLY cannot run inside a transaction — the runner defers IsConcurrent
        // changes to the post-commit phase before calling this generator.
        var concurrently = change.IsConcurrent ? "CONCURRENTLY " : "";
        var cols = string.Join(", ", idx.Columns.Select(QuoteIdentifier));
        var filter = idx.Filter is not null ? $" WHERE {idx.Filter}" : "";
        return $"CREATE {unique}INDEX {concurrently}IF NOT EXISTS {QuoteIdentifier(idx.Name)} ON {Table(change.TableName, change.SchemaName)} ({cols}){filter};\n";
    }

    // Index names live in a schema, and an unqualified DROP INDEX resolves through search_path.
    // For a table outside the default schema that silently matched nothing (IF EXISTS turns the
    // miss into a notice), leaving the stale index in place for the diff to re-propose forever.
    protected override string GenerateDropIndex(DropIndex change) =>
        $"DROP INDEX IF EXISTS {QualifiedTableName(change.IndexName, change.SchemaName ?? DefaultSchema)};\n";

    protected override string GenerateAddForeignKey(AddForeignKey change)
    {
        var fk = change.ForeignKey;
        // Escape the names used as SQL string literals, like the sibling DO-block generators do.
        var safeFkName = fk.Name.Replace("'", "''");
        var safeTableName = change.TableName.Replace("'", "''");
        return $$"""
        DO $$ BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM information_schema.table_constraints
                WHERE constraint_name = '{{safeFkName}}' AND table_schema = '{{SchemaLiteral(change.SchemaName)}}' AND table_name = '{{safeTableName}}'
            ) THEN
                ALTER TABLE {{Table(change.TableName, change.SchemaName)}} ADD CONSTRAINT {{QuoteIdentifier(fk.Name)}}
                    FOREIGN KEY ({{QuoteIdentifier(fk.Column)}}) REFERENCES {{Table(fk.ReferencedTable)}} ({{QuoteIdentifier(fk.ReferencedColumn)}})
                    ON DELETE {{ToSqlDeleteAction(fk.OnDelete)}};
            END IF;
        END $$;
        """;
    }

    protected override string GenerateDropForeignKey(DropForeignKey change) =>
        $"ALTER TABLE {Table(change.TableName, change.SchemaName)} DROP CONSTRAINT IF EXISTS {QuoteIdentifier(change.ForeignKeyName)};\n";

    protected override string GenerateAlterPrimaryKey(AlterPrimaryKey change)
    {
        // The existing constraint's name is whatever created it (ours is PK_<table>, EF Core's may
        // differ), so it is looked up in pg_constraint rather than assumed.
        var table = Table(change.TableName, change.SchemaName);
        var pkName = QuoteIdentifier($"PK_{change.TableName}");
        var cols = string.Join(", ", change.NewColumns.Select(QuoteIdentifier));

        var sb = new StringBuilder();
        sb.AppendLine("DO $$ DECLARE existing_pk text; BEGIN");
        sb.AppendLine("    SELECT conname INTO existing_pk FROM pg_constraint c");
        sb.AppendLine("    JOIN pg_class t ON t.oid = c.conrelid");
        sb.AppendLine("    JOIN pg_namespace n ON n.oid = t.relnamespace");
        sb.AppendLine($"    WHERE c.contype = 'p' AND t.relname = '{change.TableName.Replace("'", "''")}'");
        sb.AppendLine($"      AND n.nspname = '{SchemaLiteral(change.SchemaName)}';");
        sb.AppendLine("    IF existing_pk IS NOT NULL THEN");
        sb.AppendLine($"        EXECUTE format('ALTER TABLE {table} DROP CONSTRAINT %I', existing_pk);");
        sb.AppendLine("    END IF;");
        if (change.NewColumns.Length > 0)
            sb.AppendLine($"    ALTER TABLE {table} ADD CONSTRAINT {pkName} PRIMARY KEY ({cols});");
        sb.AppendLine("END $$;");
        return sb.ToString();
    }

    public override string GenerateAuditTableDdl(string auditTableName = MigrationConstants.AuditTableName)
    {
        var table = Table(auditTableName);
        var index = QuoteIdentifier($"IX_{auditTableName}_AppliedAt");
        return $"""
            CREATE TABLE IF NOT EXISTS {table} (
                "Id"           SERIAL PRIMARY KEY,
                "Hash"         varchar(64) NOT NULL,
                "SchemaJson"   text NOT NULL,
                "AppliedAt"    timestamptz NOT NULL DEFAULT now(),
                "AppliedBy"    varchar(256),
                "DurationMs"   integer,
                "ChangeCount"  integer,
                "SqlScript"    text
            );
            CREATE INDEX IF NOT EXISTS {index} ON {table} ("AppliedAt" DESC);
            """;
    }

    public override string GenerateDataMigrationTableDdl() =>
        """
        CREATE TABLE IF NOT EXISTS "__PragmaticDataMigrations" (
            "Name"       varchar(256) NOT NULL PRIMARY KEY,
            "AppliedAt"  timestamptz NOT NULL DEFAULT now(),
            "DurationMs" integer
        );
        """;

    protected override string GenerateAddCheckConstraint(AddCheckConstraint change) =>
        $"ALTER TABLE {Table(change.TableName, change.SchemaName)} ADD CONSTRAINT " +
        $"{QuoteIdentifier(change.Check.Name)} CHECK ({change.Check.Expression});\n";

    protected override string GenerateDropCheckConstraint(DropCheckConstraint change) =>
        $"ALTER TABLE {Table(change.TableName, change.SchemaName)} DROP CONSTRAINT IF EXISTS " +
        $"{QuoteIdentifier(change.CheckName)};\n";
}
