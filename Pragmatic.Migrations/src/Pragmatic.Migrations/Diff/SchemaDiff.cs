using System.Collections.Immutable;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Diff;

/// <summary>
///     Result of comparing a desired schema against the current database state.
/// </summary>
/// <param name="Changes">Ordered list of schema changes to apply.</param>
/// <param name="HasBreakingChanges">True if any change may cause data loss.</param>
/// <param name="DesiredSchema">The target schema — used by SQLite for table rebuild on ALTER COLUMN.</param>
public sealed record SchemaDiff(
    ImmutableArray<SchemaChange> Changes,
    bool HasBreakingChanges,
    SchemaVersion? DesiredSchema = null)
{
    /// <summary>No changes needed — schemas are identical.</summary>
    public static SchemaDiff Empty { get; } = new([], false);

    /// <summary>Whether there are any changes to apply.</summary>
    public bool HasChanges => Changes.Length > 0;
}
