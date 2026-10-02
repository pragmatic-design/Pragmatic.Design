using System.Collections.Immutable;

namespace Pragmatic.Migrations.Diff.Changes;

/// <summary>
///     Change the columns that make up a table's primary key.
/// </summary>
/// <remarks>
///     Always breaking. Replacing a primary key drops the constraint that guarantees uniqueness,
///     and the new one fails if the existing rows do not satisfy it — so it needs the same explicit
///     Force as a DROP. Foreign keys pointing at the old key must be dropped first, which the
///     ordering in <see cref="SchemaDiffEngine" /> takes care of.
/// </remarks>
/// <param name="TableName">Table whose primary key changes.</param>
/// <param name="OldColumns">Columns currently forming the primary key (empty when there was none).</param>
/// <param name="NewColumns">Columns the primary key must consist of.</param>
/// <param name="SchemaName">Database schema the table lives in, or null for the provider default.</param>
public sealed record AlterPrimaryKey(
    string TableName,
    ImmutableArray<string> OldColumns,
    ImmutableArray<string> NewColumns,
    string? SchemaName = null)
    : SchemaChange(
        $"ALTER PRIMARY KEY {TableName} ({string.Join(", ", OldColumns)} → {string.Join(", ", NewColumns)})",
        IsBreaking: true);
