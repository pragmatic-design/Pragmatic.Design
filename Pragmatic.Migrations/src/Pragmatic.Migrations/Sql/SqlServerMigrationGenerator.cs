using System.Text;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Sql;

/// <summary>
///     SQL Server SQL generator. Uses IF NOT EXISTS patterns with sys.* catalog views.
/// </summary>
public sealed class SqlServerMigrationGenerator : SqlGeneratorBase
{
    /// <summary>
    ///     Schema used for tables that do not declare one. Defaults to "dbo" (the SQL Server
    ///     default). Configurable for hosts that place their objects in a non-default schema.
    /// </summary>
    public string DefaultSchema { get; init; } = "dbo";

    public override string ProviderName => MigrationConstants.ProviderSqlServer;

    // Double any embedded "]": it would otherwise close the bracketed identifier early.
    protected override string QuoteIdentifier(string name) => $"[{name.Replace("]", "]]")}]";

    private string Table(string name, string? schema = null) => QualifiedTableName(name, schema ?? DefaultSchema);

    protected override string GenerateCreateTable(CreateTable change)
    {
        var t = change.Table;
        var schema = t.SchemaName ?? DefaultSchema;
        var sb = new StringBuilder();
        sb.AppendLine($"IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '{t.Name}' AND schema_id = SCHEMA_ID('{schema}'))");
        sb.AppendLine("BEGIN");
        sb.AppendLine($"    CREATE TABLE {Table(t.Name, schema)} (");

        var cols = t.Columns.Select(c => $"        {ColumnDef(c, QuoteIdentifier)}").ToList();

        var pk = t.Columns.Where(c => c.IsPrimaryKey).Select(c => QuoteIdentifier(c.Name)).ToList();
        if (pk.Count > 0)
            cols.Add($"        CONSTRAINT [PK_{t.Name}] PRIMARY KEY ({string.Join(", ", pk)})");

        sb.AppendLine(string.Join(",\n", cols));
        sb.AppendLine("    );");
        sb.AppendLine("END;");
        return sb.ToString();
    }

    protected override string GenerateDropTable(DropTable change) =>
        $"DROP TABLE IF EXISTS {Table(change.TableName, change.SchemaName)};\n";

    protected override string GenerateAddColumn(AddColumn change) =>
        $$"""
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID('{{Table(change.TableName, change.SchemaName)}}') AND name = '{{change.Column.Name.Replace("'", "''")}}'
        )
        BEGIN
            ALTER TABLE {{Table(change.TableName, change.SchemaName)}} ADD {{ColumnDef(change.Column, QuoteIdentifier)}};
        END;
        """;

    protected override string GenerateDropColumn(DropColumn change) =>
        $$"""
        IF EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID('{{Table(change.TableName, change.SchemaName)}}') AND name = '{{change.ColumnName.Replace("'", "''")}}'
        )
        BEGIN
            ALTER TABLE {{Table(change.TableName, change.SchemaName)}} DROP COLUMN {{QuoteIdentifier(change.ColumnName)}};
        END;
        """;

    protected override string GenerateRenameColumn(RenameColumn change) =>
        $$"""
        IF EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID('{{Table(change.TableName, change.SchemaName)}}') AND name = '{{change.OldName.Replace("'", "''")}}'
        ) AND NOT EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID('{{Table(change.TableName, change.SchemaName)}}') AND name = '{{change.NewName.Replace("'", "''")}}'
        )
        BEGIN
            EXEC sp_rename '{{Table(change.TableName, change.SchemaName)}}.{{change.OldName}}', '{{change.NewName}}', 'COLUMN';
        END;
        """;

    protected override string GenerateAlterColumnType(AlterColumnType change)
    {
        // SQL Server's ALTER COLUMN restates the full column definition, and an omitted NULL/NOT
        // NULL means NULL. Emitting just the type therefore DROPS an existing NOT NULL constraint
        // silently — the diff would then see a nullability mismatch on the next run and try to put
        // it back as a breaking change. Restate it, and refuse to guess when it is unknown.
        if (change.IsNullable is not { } isNullable)
            throw new InvalidOperationException(
                $"Cannot generate the type change for {change.TableName}.{change.ColumnName}: the target " +
                "nullability is unknown. SQL Server ALTER COLUMN restates the whole definition and would " +
                "silently make the column nullable; refusing to alter it blindly.");

        var nullSpec = isNullable ? "NULL" : "NOT NULL";
        return $"ALTER TABLE {Table(change.TableName, change.SchemaName)} ALTER COLUMN {QuoteIdentifier(change.ColumnName)} {change.NewType} {nullSpec};\n";
    }

    protected override string GenerateAlterColumnNullability(AlterColumnNullability change)
    {
        // SQL Server's ALTER COLUMN must restate the full column type. If the real type is not
        // known we must NOT fabricate one (a former fallback to nvarchar(max) silently rewrote
        // the column's type — data corruption / truncation risk). Surface it as a hard error in
        // the generated script instead so the migration aborts rather than altering blindly.
        if (string.IsNullOrEmpty(change.ColumnType))
            throw new InvalidOperationException(
                $"Cannot generate nullability change for {change.TableName}.{change.ColumnName}: the column type is unknown. " +
                "SQL Server ALTER COLUMN must restate the existing type; refusing to fabricate one (would silently change the column type).");

        var nullSpec = change.NewIsNullable ? "NULL" : "NOT NULL";
        return $"""
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('{Table(change.TableName, change.SchemaName)}') AND name = '{change.ColumnName}')
                ALTER TABLE {Table(change.TableName, change.SchemaName)} ALTER COLUMN {QuoteIdentifier(change.ColumnName)} {change.ColumnType} {nullSpec};

            """;
    }

    protected override string GenerateAlterColumnDefault(AlterColumnDefault change)
    {
        var table = Table(change.TableName, change.SchemaName);
        var tableLiteral = table.Replace("'", "''");
        var columnLiteral = change.ColumnName.Replace("'", "''");

        // SQL Server has no "replace the default": ADD DEFAULT on a column that already has one
        // fails with Msg 1781. Since AlterColumnDefault is emitted precisely when the default
        // CHANGED, the old constraint is almost always there — always drop it first (its name is
        // auto-generated, so it has to be looked up), then add the new one when there is one.
        var sb = new StringBuilder();
        sb.AppendLine("DECLARE @constraint nvarchar(256);");
        sb.AppendLine("SELECT @constraint = dc.name FROM sys.default_constraints dc");
        sb.AppendLine("JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id");
        sb.AppendLine($"WHERE dc.parent_object_id = OBJECT_ID(N'{tableLiteral}')");
        sb.AppendLine($"  AND c.name = N'{columnLiteral}';");
        sb.AppendLine("IF @constraint IS NOT NULL");
        sb.AppendLine($"    EXEC(N'ALTER TABLE {tableLiteral} DROP CONSTRAINT [' + @constraint + N']');");

        if (change.NewDefault is not null)
            sb.AppendLine($"ALTER TABLE {table} ADD DEFAULT {change.NewDefault} FOR {QuoteIdentifier(change.ColumnName)};");

        return sb.ToString();
    }

    protected override string GenerateAddIndex(AddIndex change)
    {
        var idx = change.Index;
        var unique = idx.IsUnique ? "UNIQUE " : "";
        var cols = string.Join(", ", idx.Columns.Select(QuoteIdentifier));
        var filter = idx.Filter is not null ? $" WHERE {idx.Filter}" : "";
        // WITH (ONLINE = ON) requires SQL Server Enterprise — it builds the index without
        // long table locks. The runner defers IsConcurrent changes to the post-commit
        // phase so a failed online build leaves the schema intact.
        var with = change.IsConcurrent ? " WITH (ONLINE = ON)" : "";
        return $$"""
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{{idx.Name}}' AND object_id = OBJECT_ID('{{Table(change.TableName, change.SchemaName)}}'))
        BEGIN
            CREATE {{unique}}INDEX {{QuoteIdentifier(idx.Name)}} ON {{Table(change.TableName, change.SchemaName)}} ({{cols}}){{filter}}{{with}};
        END;
        """;
    }

    protected override string GenerateDropIndex(DropIndex change) =>
        $"DROP INDEX IF EXISTS {QuoteIdentifier(change.IndexName)} ON {Table(change.TableName, change.SchemaName)};\n";

    protected override string GenerateAddForeignKey(AddForeignKey change)
    {
        var fk = change.ForeignKey;
        var onDelete = ToSqlDeleteAction(fk.OnDelete);
        return $$"""
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = '{{fk.Name}}')
        BEGIN
            ALTER TABLE {{Table(change.TableName, change.SchemaName)}} ADD CONSTRAINT {{QuoteIdentifier(fk.Name)}}
                FOREIGN KEY ({{QuoteIdentifier(fk.Column)}}) REFERENCES {{Table(fk.ReferencedTable)}} ({{QuoteIdentifier(fk.ReferencedColumn)}})
                ON DELETE {{onDelete}};
        END;
        """;
    }

    protected override string GenerateDropForeignKey(DropForeignKey change) =>
        $$"""
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = '{{change.ForeignKeyName}}')
        BEGIN
            ALTER TABLE {{Table(change.TableName, change.SchemaName)}} DROP CONSTRAINT {{QuoteIdentifier(change.ForeignKeyName)}};
        END;
        """;

    protected override string GenerateAlterPrimaryKey(AlterPrimaryKey change)
    {
        var table = Table(change.TableName, change.SchemaName);
        var tableLiteral = table.Replace("'", "''");
        var pkName = QuoteIdentifier($"PK_{change.TableName}");
        var cols = string.Join(", ", change.NewColumns.Select(QuoteIdentifier));

        // The existing key's name is whatever created it, so look it up instead of assuming ours.
        var sb = new StringBuilder();
        sb.AppendLine("DECLARE @pk nvarchar(256);");
        sb.AppendLine("SELECT @pk = name FROM sys.key_constraints");
        sb.AppendLine($"WHERE type = 'PK' AND parent_object_id = OBJECT_ID(N'{tableLiteral}');");
        sb.AppendLine("IF @pk IS NOT NULL");
        sb.AppendLine($"    EXEC(N'ALTER TABLE {tableLiteral} DROP CONSTRAINT [' + @pk + N']');");
        if (change.NewColumns.Length > 0)
            sb.AppendLine($"ALTER TABLE {table} ADD CONSTRAINT {pkName} PRIMARY KEY ({cols});");
        return sb.ToString();
    }

    public override string GenerateAuditTableDdl(string auditTableName = MigrationConstants.AuditTableName)
    {
        var table = Table(auditTableName);
        var index = QuoteIdentifier($"IX_{auditTableName}_AppliedAt");
        return $"""
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '{auditTableName.Replace("'", "''")}' AND schema_id = SCHEMA_ID('{DefaultSchema.Replace("'", "''")}'))
            BEGIN
                CREATE TABLE {table} (
                    [Id]           int IDENTITY(1,1) PRIMARY KEY,
                    [Hash]         nvarchar(64) NOT NULL,
                    [SchemaJson]   nvarchar(MAX) NOT NULL,
                    [AppliedAt]    datetimeoffset NOT NULL DEFAULT SYSUTCDATETIME(),
                    [AppliedBy]    nvarchar(256) NULL,
                    [DurationMs]   int NULL,
                    [ChangeCount]  int NULL,
                    [SqlScript]    nvarchar(MAX) NULL
                );
                CREATE INDEX {index} ON {table} ([AppliedAt] DESC);
            END;
            """;
    }

    public override string GenerateDataMigrationTableDdl() =>
        """
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '__PragmaticDataMigrations')
        BEGIN
            CREATE TABLE [dbo].[__PragmaticDataMigrations] (
                [Name]       nvarchar(256) NOT NULL PRIMARY KEY,
                [AppliedAt]  datetimeoffset NOT NULL DEFAULT SYSUTCDATETIME(),
                [DurationMs] int NULL
            );
        END;
        """;

    protected override string GenerateAddCheckConstraint(AddCheckConstraint change) =>
        $"ALTER TABLE {Table(change.TableName, change.SchemaName)} ADD CONSTRAINT " +
        $"{QuoteIdentifier(change.Check.Name)} CHECK ({change.Check.Expression});\n";

    protected override string GenerateDropCheckConstraint(DropCheckConstraint change) =>
        $"ALTER TABLE {Table(change.TableName, change.SchemaName)} DROP CONSTRAINT IF EXISTS " +
        $"{QuoteIdentifier(change.CheckName)};\n";
}
