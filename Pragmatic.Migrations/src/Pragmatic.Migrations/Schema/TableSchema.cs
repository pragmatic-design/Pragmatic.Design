using System.Collections.Immutable;

namespace Pragmatic.Migrations.Schema;

/// <summary>
///     Table definition in a database schema.
/// </summary>
/// <param name="Name">Table name (pluralized entity name by convention).</param>
/// <param name="SchemaName">Database schema ("public" for PG, "dbo" for SQL Server, null for SQLite).</param>
/// <param name="Columns">All columns in the table.</param>
/// <param name="Indexes">All indexes on the table.</param>
/// <param name="ForeignKeys">All foreign key constraints.</param>
/// <param name="CheckConstraints">
///     Row-level invariants the database enforces. Optional so the three introspectors and every
///     existing caller keep compiling; a schema read from a database that does not report them arrives
///     empty rather than wrong.
/// </param>
public sealed record TableSchema(
    string Name,
    string? SchemaName,
    ImmutableArray<ColumnSchema> Columns,
    ImmutableArray<IndexSchema> Indexes,
    ImmutableArray<ForeignKeySchema> ForeignKeys,
    ImmutableArray<CheckConstraintSchema> CheckConstraints = default)
{
    /// <summary>
    ///     Row-level invariants the database enforces. Never <c>default</c>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Normalised here rather than exposed as a second property beside the parameter. The first
    ///     version did the latter, and both were wrong in the same way: a default <c>ImmutableArray</c>
    ///     throws the moment anything enumerates it — which every caller predating checks produces, and
    ///     which reached the migration runner as "this operation cannot be performed on a default
    ///     instance" — and a second public property made the committed schema snapshot carry the same
    ///     list twice, once per name, in every table.
    /// </remarks>
    public ImmutableArray<CheckConstraintSchema> CheckConstraints { get; init; } =
        CheckConstraints.IsDefault ? ImmutableArray<CheckConstraintSchema>.Empty : CheckConstraints;
}
