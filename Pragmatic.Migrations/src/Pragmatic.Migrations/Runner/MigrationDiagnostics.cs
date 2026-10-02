using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Migrations.Runner;

/// <summary>
///     Structured logging and OpenTelemetry diagnostics for the migration runner.
/// </summary>
internal static partial class MigrationDiagnostics
{
    internal const string SourceName = "Pragmatic.Migrations";

    // The ActivitySource and Meter are process-lifetime singletons that back all migration
    // telemetry. They are intentionally NOT disposed: as static fields in library code there is
    // no deterministic shutdown hook, and both types are cheap, thread-safe, and designed to live
    // for the lifetime of the process (matching the BCL/OpenTelemetry guidance for static sources).
    // Disposing them would risk tearing down telemetry while a migration is still emitting.
    internal static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");
    internal static readonly Meter Meter = new(SourceName, "1.0.0");

    private static readonly Counter<long> MigrationsExecuted = Meter.CreateCounter<long>("pragmatic.migrations.executed", "migrations");
    private static readonly Counter<long> MigrationsFailed = Meter.CreateCounter<long>("pragmatic.migrations.failed", "migrations");
    private static readonly Counter<long> ChangesAppliedCounter = Meter.CreateCounter<long>("pragmatic.migrations.changes_applied", "changes");
    private static readonly Histogram<double> MigrationDuration = Meter.CreateHistogram<double>("pragmatic.migrations.duration", "ms");

    /// <summary>
    ///     Records the outcome of one database's migration run: run count, failures, changes
    ///     applied and duration, all tagged with the database and provider.
    /// </summary>
    internal static void RecordRun(string databaseName, string? providerName, bool success, int changesApplied, double durationMs)
    {
        var tags = new TagList
        {
            { "db.name", databaseName },
            { "db.provider", providerName ?? "unknown" }
        };

        MigrationsExecuted.Add(1, tags);
        MigrationDuration.Record(durationMs, tags);

        if (success)
        {
            if (changesApplied > 0)
                ChangesAppliedCounter.Add(changesApplied, tags);
        }
        else
        {
            MigrationsFailed.Add(1, tags);
        }
    }

    // =========================================================================
    // Migration lifecycle
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Information, Message = "[{DatabaseName}] Starting migration ({ProviderName}) — desired hash: {DesiredHash}")]
    internal static partial void MigrationStarted(ILogger logger, string databaseName, string? providerName, string desiredHash);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{DatabaseName}] Schema up to date — no changes needed (hash: {Hash})")]
    internal static partial void SchemaUpToDate(ILogger logger, string databaseName, string hash);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{DatabaseName}] Diff computed: {ChangeCount} changes ({BreakingCount} breaking)")]
    internal static partial void DiffComputed(ILogger logger, string databaseName, int changeCount, int breakingCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{DatabaseName}] Migration complete: {ChangeCount} changes applied in {DurationMs}ms")]
    internal static partial void MigrationComplete(ILogger logger, string databaseName, int changeCount, long durationMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{DatabaseName}] Dry run: {ChangeCount} changes generated")]
    internal static partial void DryRunComplete(ILogger logger, string databaseName, int changeCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{DatabaseName}] Skipped (not in database filter)")]
    internal static partial void MigrationSkipped(ILogger logger, string databaseName);

    // =========================================================================
    // Per-change execution
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DatabaseName}] Applying {StepIndex}/{TotalSteps}: {ChangeDescription}")]
    internal static partial void ApplyingChange(ILogger logger, string databaseName, int stepIndex, int totalSteps, string changeDescription);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DatabaseName}] Applied {StepIndex}/{TotalSteps}: {ChangeDescription}")]
    internal static partial void ChangeApplied(ILogger logger, string databaseName, int stepIndex, int totalSteps, string changeDescription);

    // =========================================================================
    // Errors
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Error, Message = "[{DatabaseName}] Migration failed at step {StepIndex}/{TotalSteps}: {ChangeDescription}")]
    internal static partial void MigrationFailed(ILogger logger, Exception exception, string databaseName, int stepIndex, int totalSteps, string changeDescription);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[{DatabaseName}] Blocked: {BreakingCount} breaking changes require Force=true")]
    internal static partial void BreakingChangesBlocked(ILogger logger, string databaseName, int breakingCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "[{DatabaseName}] Migration leader finished but the schema is still {PendingChanges} change(s) behind — the leader's migration likely failed")]
    internal static partial void FollowerSchemaStale(ILogger logger, string databaseName, int pendingChanges);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[{DatabaseName}] Transaction rollback failed")]
    internal static partial void RollbackFailed(ILogger logger, Exception exception, string databaseName);
}
