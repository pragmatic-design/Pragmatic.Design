namespace Pragmatic.Migrations.Schema;

/// <summary>
///     Column definition in a database table schema.
/// </summary>
/// <param name="Name">Column name (matches entity property name by convention).</param>
/// <param name="SqlType">Provider-specific SQL type (e.g. "uuid", "nvarchar(256)", "TEXT").</param>
/// <param name="IsNullable">Whether the column allows NULL values.</param>
/// <param name="IsPrimaryKey">Whether this column is part of the primary key.</param>
/// <param name="DefaultValue">SQL default expression (e.g. "false", "0", "now()"). Null if no default.</param>
/// <param name="RenamedFrom">Previous column name, if renamed. Used by diff engine to detect renames vs drop+add.</param>
public sealed record ColumnSchema(
    string Name,
    string SqlType,
    bool IsNullable,
    bool IsPrimaryKey,
    string? DefaultValue = null,
    string? RenamedFrom = null);
