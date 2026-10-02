namespace Pragmatic.Telemetry.Conventions;

/// <summary>
///     Tag names for schema migrations.
/// </summary>
public static class MigrationTags
{
    /// <summary>Number of changes in the migration.</summary>
    public const string ChangeCount = "pragmatic.migrations.change_count";

    /// <summary>Number of changes classified as breaking.</summary>
    public const string BreakingCount = "pragmatic.migrations.breaking_count";

    /// <summary>Migration duration in milliseconds.</summary>
    public const string DurationMs = "pragmatic.migrations.duration_ms";
}
