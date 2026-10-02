using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Diff.Changes;

/// <summary>Add a new column to an existing table.</summary>
/// <remarks>
///     Breaking when the column is NOT NULL with no default: existing rows cannot satisfy the
///     constraint, so the ALTER fails (or corrupts) unless the column is nullable or has a default.
/// </remarks>
public sealed record AddColumn(string TableName, ColumnSchema Column, string? SchemaName = null)
    : SchemaChange($"ADD COLUMN {TableName}.{Column.Name}",
        IsBreaking: !Column.IsNullable && Column.DefaultValue is null);

/// <summary>Drop a column from a table. Always breaking — causes data loss.</summary>
public sealed record DropColumn(string TableName, string ColumnName, string? SchemaName = null)
    : SchemaChange($"DROP COLUMN {TableName}.{ColumnName}", IsBreaking: true);

/// <summary>Rename a column. Detected via <see cref="ColumnSchema.RenamedFrom" />.</summary>
public sealed record RenameColumn(string TableName, string OldName, string NewName, string? SchemaName = null)
    : SchemaChange($"RENAME {TableName}.{OldName} → {NewName}", IsBreaking: false);

/// <summary>Change a column's SQL type. Breaking when narrowing (e.g. text → varchar(50)).</summary>
/// <param name="TableName">Table the column belongs to.</param>
/// <param name="ColumnName">Column being altered.</param>
/// <param name="OldType">Current SQL type in the database.</param>
/// <param name="NewType">Target SQL type.</param>
/// <param name="IsNarrowing">Whether the conversion can lose data (makes the change breaking).</param>
/// <param name="SchemaName">Database schema the table lives in, or null for the provider default.</param>
/// <param name="IsNullable">
///     Nullability the column must keep across the type change. SQL Server's <c>ALTER COLUMN</c>
///     restates the whole column definition and treats an omitted NULL/NOT NULL as "nullable", so a
///     type change would silently drop a NOT NULL constraint that nobody asked to drop. Null means
///     "unknown" — providers that need it fail rather than guess.
/// </param>
public sealed record AlterColumnType(
    string TableName,
    string ColumnName,
    string OldType,
    string NewType,
    bool IsNarrowing,
    string? SchemaName = null,
    bool? IsNullable = null)
    : SchemaChange($"ALTER TYPE {TableName}.{ColumnName} ({OldType} → {NewType})", IsBreaking: IsNarrowing);

/// <summary>Change a column's nullability (SET NOT NULL or DROP NOT NULL).</summary>
public sealed record AlterColumnNullability(string TableName, string ColumnName, bool NewIsNullable, string ColumnType = "", string? SchemaName = null)
    : SchemaChange(
        $"{(NewIsNullable ? "SET NULL" : "SET NOT NULL")} {TableName}.{ColumnName}",
        IsBreaking: !NewIsNullable); // SET NOT NULL is breaking if existing data has nulls

/// <summary>Change or drop a column's default value.</summary>
public sealed record AlterColumnDefault(string TableName, string ColumnName, string? OldDefault, string? NewDefault, string? SchemaName = null)
    : SchemaChange($"ALTER DEFAULT {TableName}.{ColumnName}", IsBreaking: false);
