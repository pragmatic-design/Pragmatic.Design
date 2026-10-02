using System.Collections.Immutable;
using Pragmatic.Migrations.Diff.Changes;

namespace Pragmatic.Migrations.Runner;

/// <summary>
///     Result of a migration run.
/// </summary>
/// <param name="Success">Whether the migration completed successfully.</param>
/// <param name="ChangesApplied">Number of schema changes applied.</param>
/// <param name="Duration">Total duration of the migration.</param>
/// <param name="GeneratedSql">The SQL script that was (or would be) executed.</param>
/// <param name="AppliedChanges">The list of changes that were applied.</param>
/// <param name="Error">Error message if the migration failed.</param>
public sealed record MigrationResult(
    bool Success,
    int ChangesApplied,
    TimeSpan Duration,
    string? GeneratedSql,
    ImmutableArray<SchemaChange> AppliedChanges,
    string? Error)
{
    /// <summary>Schemas are identical — no changes needed.</summary>
    public static MigrationResult NoChanges { get; } = new(true, 0, TimeSpan.Zero, null, [], null);

    /// <summary>Index of the change that failed (0-based), or null if all succeeded.</summary>
    public int? FailedChangeIndex { get; init; }

    /// <summary>SQL statement that caused the failure, or null if all succeeded.</summary>
    public string? FailedChangeSql { get; init; }

    /// <summary>Context-aware recovery suggestions for the developer.</summary>
    public ImmutableArray<string> Suggestions { get; init; } = [];
}
