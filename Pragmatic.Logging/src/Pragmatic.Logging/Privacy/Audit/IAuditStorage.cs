using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Privacy.Audit;

/// <summary>
/// Interface for audit storage implementations.
/// Provides pluggable storage backends for audit entries.
/// </summary>
public interface IAuditStorage : IDisposable
{
    /// <summary>
    /// Stores a batch of audit entries.
    /// </summary>
    /// <param name="entries">The entries to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task StoreEntriesAsync(IReadOnlyList<AuditEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets audit entries for a specific time period.
    /// </summary>
    /// <param name="from">Start time (inclusive)</param>
    /// <param name="until">End time (exclusive)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of audit entries</returns>
    Task<IReadOnlyList<AuditEntry>> GetEntriesAsync(DateTime from, DateTime until, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of audit entries for a specific time period.
    /// </summary>
    /// <param name="from">Start time (inclusive)</param>
    /// <param name="until">End time (exclusive)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Count of entries</returns>
    Task<long> GetEntryCountAsync(DateTime from, DateTime until, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes audit entries older than the specified date (for retention policies).
    /// </summary>
    /// <param name="before">Delete entries before this date</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of entries deleted</returns>
    Task<long> DeleteEntriesAsync(DateTime before, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a health check on the storage backend.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health check result</returns>
    Task<AuditStorageHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for advanced audit queries.
/// </summary>
public interface IAuditQuery
{
    /// <summary>
    /// Executes a complex audit query.
    /// </summary>
    /// <param name="query">The query to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Query results</returns>
    Task<AuditQueryResult> QueryAsync(AuditQueryBuilder query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets audit statistics for a time period.
    /// </summary>
    /// <param name="from">Start time</param>
    /// <param name="until">End time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Audit statistics</returns>
    Task<AuditStatistics> GetStatisticsAsync(DateTime from, DateTime until, CancellationToken cancellationToken = default);
}

/// <summary>
/// Health result for audit storage.
/// </summary>
public sealed class AuditStorageHealthResult
{
    /// <summary>Gets or sets whether the storage is healthy.</summary>
    public bool IsHealthy { get; set; }

    /// <summary>Gets or sets the response time in milliseconds.</summary>
    public double ResponseTimeMs { get; set; }

    /// <summary>Gets or sets any error message.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Gets or sets additional health details.</summary>
    public Dictionary<string, object?> Details { get; set; } = new();
}

/// <summary>
/// Audit query result containing entries and metadata.
/// </summary>
public sealed class AuditQueryResult
{
    /// <summary>Gets or sets the matching audit entries.</summary>
    public IReadOnlyList<AuditEntry> Entries { get; set; } = Array.Empty<AuditEntry>();

    /// <summary>Gets or sets the total count of matching entries (for pagination).</summary>
    public long TotalCount { get; set; }

    /// <summary>Gets or sets whether there are more results available.</summary>
    public bool HasMore { get; set; }

    /// <summary>Gets or sets the query execution time in milliseconds.</summary>
    public double ExecutionTimeMs { get; set; }
}

/// <summary>
/// Statistical information about audit entries.
/// </summary>
public sealed class AuditStatistics
{
    /// <summary>Gets or sets the total number of entries.</summary>
    public long TotalEntries { get; set; }

    /// <summary>Gets or sets entry counts by event type.</summary>
    public Dictionary<AuditEventType, long> EntriesByEventType { get; set; } = new();

    /// <summary>Gets or sets entry counts by compliance standard.</summary>
    public Dictionary<ComplianceStandard, long> EntriesByComplianceStandard { get; set; } = new();

    /// <summary>Gets or sets entry counts by severity.</summary>
    public Dictionary<AuditSeverity, long> EntriesBySeverity { get; set; } = new();

    /// <summary>Gets or sets the most active users.</summary>
    public Dictionary<string, long> TopUsersByActivity { get; set; } = new();

    /// <summary>Gets or sets the time range covered by the statistics.</summary>
    public TimeSpan CoverageTimeSpan { get; set; }
}