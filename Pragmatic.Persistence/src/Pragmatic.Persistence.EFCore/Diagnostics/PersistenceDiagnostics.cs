using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pragmatic.Persistence.EFCore.Diagnostics;

/// <summary>
///     Centralized diagnostics for Pragmatic.Persistence.EFCore: ActivitySource and Meter with instruments.
/// </summary>
public static class PersistenceDiagnostics
{
    /// <summary>The source name for all Persistence activities.</summary>
    public const string SourceName = "Pragmatic.Persistence";

    /// <summary>The ActivitySource for distributed tracing.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");

    /// <summary>The Meter for metrics collection.</summary>
    public static readonly Meter Meter = new(SourceName, "1.0.0");

    /// <summary>Histogram of query execution duration in milliseconds.</summary>
    public static readonly Histogram<double> QueryDuration = Meter.CreateHistogram<double>(
        "pragmatic.persistence.query_duration",
        unit: "ms",
        description: "Duration of query execution");

    /// <summary>Counter of total queries executed.</summary>
    public static readonly Counter<long> QueriesExecuted = Meter.CreateCounter<long>(
        "pragmatic.persistence.queries",
        description: "Total queries executed");

    /// <summary>Counter of query failures.</summary>
    public static readonly Counter<long> QueryFailures = Meter.CreateCounter<long>(
        "pragmatic.persistence.query_failures",
        description: "Total query failures");

    /// <summary>Histogram of SaveChanges duration in milliseconds.</summary>
    public static readonly Histogram<double> SaveChangesDuration = Meter.CreateHistogram<double>(
        "pragmatic.persistence.save_duration",
        unit: "ms",
        description: "Duration of SaveChanges");

    /// <summary>Counter of total rows affected by SaveChanges.</summary>
    public static readonly Counter<long> RowsAffected = Meter.CreateCounter<long>(
        "pragmatic.persistence.rows_affected",
        description: "Total rows affected by SaveChanges");

    // ── Bulk Operations ─────────────────────────────────────────────────

    /// <summary>Histogram of bulk insert duration in milliseconds.</summary>
    public static readonly Histogram<double> BulkInsertDuration = Meter.CreateHistogram<double>(
        "pragmatic.persistence.bulk_insert_duration",
        unit: "ms",
        description: "Duration of bulk insert operations");

    /// <summary>Histogram of bulk upsert duration in milliseconds.</summary>
    public static readonly Histogram<double> BulkUpsertDuration = Meter.CreateHistogram<double>(
        "pragmatic.persistence.bulk_upsert_duration",
        unit: "ms",
        description: "Duration of bulk upsert operations");

    /// <summary>Counter of total bulk operations executed.</summary>
    public static readonly Counter<long> BulkOperations = Meter.CreateCounter<long>(
        "pragmatic.persistence.bulk_operations",
        description: "Total bulk operations executed");

    /// <summary>Counter of bulk operation failures.</summary>
    public static readonly Counter<long> BulkFailures = Meter.CreateCounter<long>(
        "pragmatic.persistence.bulk_failures",
        description: "Total bulk operation failures");
}
