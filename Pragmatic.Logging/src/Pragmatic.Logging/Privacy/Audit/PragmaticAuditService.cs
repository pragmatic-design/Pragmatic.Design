using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Privacy.Audit;

/// <summary>
/// Main audit service that coordinates audit operations.
/// Replaces the singleton AuditTrail with a proper DI-based service.
/// </summary>
public sealed class PragmaticAuditService : IDisposable, IHostedService
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IAuditStorage _storage;
    private readonly IAuditPolicy _policy;
    private readonly ConcurrentQueue<AuditEntry> _auditQueue = new();
    private readonly Timer _flushTimer;
    private readonly AuditServiceOptions _options;
    private readonly ILogger<PragmaticAuditService> _logger;
    private volatile bool _disposed;
    private CancellationTokenSource? _shutdownTokenSource;

    public PragmaticAuditService(
        IAuditStorage storage,
        IAuditPolicy policy,
        AuditServiceOptions options,
        ILogger<PragmaticAuditService> logger)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _flushTimer = new Timer(FlushAuditEntries, null, _options.FlushInterval, _options.FlushInterval);
    }

    /// <summary>
    /// Records a redaction event in the audit trail.
    /// </summary>
    /// <param name="logLevel">The log level</param>
    /// <param name="categoryName">The category name</param>
    /// <param name="propertyName">The property name that was redacted</param>
    /// <param name="originalLength">The original length of the redacted value</param>
    /// <param name="redactionReason">The reason for redaction</param>
    /// <param name="complianceStandard">The compliance standard that triggered redaction</param>
    /// <param name="userId">The user ID if available</param>
    /// <param name="correlationId">The correlation ID if available</param>
    public void RecordRedaction(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        string categoryName,
        string propertyName,
        int originalLength,
        RedactionReason redactionReason,
        ComplianceStandard complianceStandard,
        string? userId = null,
        string? correlationId = null)
    {
        if (_disposed)
            return;

        var context = new AuditContext
        {
            EventType = AuditEventType.DataRedaction,
            ComplianceStandard = complianceStandard,
            Severity = DetermineSeverityFromReason(redactionReason),
            LogLevel = logLevel,
            CategoryName = categoryName,
            UserId = userId
        };

        if (!_policy.ShouldAudit(context))
            return;

        var entry = new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.DataRedaction,
            LogLevel = logLevel.ToString(),
            CategoryName = categoryName,
            PropertyName = propertyName,
            OriginalLength = originalLength,
            RedactionReason = redactionReason,
            ComplianceStandard = complianceStandard,
            UserId = userId,
            CorrelationId = correlationId,
            ThreadId = Environment.CurrentManagedThreadId,
            ProcessId = Environment.ProcessId,
            Severity = context.Severity
        };

        entry = _policy.TransformEntry(entry);
        EnqueueEntry(entry);
    }

    /// <summary>
    /// Records a sensitive data access event.
    /// </summary>
    /// <param name="userId">The user accessing the data</param>
    /// <param name="dataType">The type of sensitive data accessed</param>
    /// <param name="accessReason">The reason for access</param>
    /// <param name="correlationId">The correlation ID</param>
    /// <param name="complianceStandard">The compliance standard</param>
    public void RecordSensitiveDataAccess(
        string userId,
        string dataType,
        string accessReason,
        string? correlationId = null,
        ComplianceStandard complianceStandard = ComplianceStandard.General)
    {
        if (_disposed)
            return;

        var context = new AuditContext
        {
            EventType = AuditEventType.SensitiveDataAccess,
            ComplianceStandard = complianceStandard,
            Severity = AuditSeverity.Medium,
            UserId = userId
        };

        if (!_policy.ShouldAudit(context))
            return;

        var entry = new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.SensitiveDataAccess,
            UserId = userId,
            DataType = dataType,
            AccessReason = accessReason,
            CorrelationId = correlationId,
            ComplianceStandard = complianceStandard,
            ThreadId = Environment.CurrentManagedThreadId,
            ProcessId = Environment.ProcessId,
            Severity = AuditSeverity.Medium
        };

        entry = _policy.TransformEntry(entry);
        EnqueueEntry(entry);
    }

    /// <summary>
    /// Records a compliance violation event.
    /// </summary>
    /// <param name="violationType">The type of violation</param>
    /// <param name="description">Description of the violation</param>
    /// <param name="complianceStandard">The compliance standard violated</param>
    /// <param name="severity">The severity of the violation</param>
    /// <param name="userId">The user involved if applicable</param>
    /// <param name="correlationId">The correlation ID</param>
    public void RecordComplianceViolation(
        string violationType,
        string description,
        ComplianceStandard complianceStandard,
        AuditSeverity severity,
        string? userId = null,
        string? correlationId = null)
    {
        if (_disposed)
            return;

        var context = new AuditContext
        {
            EventType = AuditEventType.ComplianceViolation,
            ComplianceStandard = complianceStandard,
            Severity = severity,
            UserId = userId,
            IsCriticalSecurityEvent = severity >= AuditSeverity.Critical
        };

        // Compliance violations are always audited
        var entry = new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.ComplianceViolation,
            ViolationType = violationType,
            Description = description,
            ComplianceStandard = complianceStandard,
            Severity = severity,
            UserId = userId,
            CorrelationId = correlationId,
            ThreadId = Environment.CurrentManagedThreadId,
            ProcessId = Environment.ProcessId
        };

        entry = _policy.TransformEntry(entry);
        EnqueueEntry(entry, forceImmediateFlush: true);
    }

    /// <summary>
    /// Gets audit entries for a specific time period.
    /// </summary>
    /// <param name="from">Start time</param>
    /// <param name="until">End time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of audit entries</returns>
    public async Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(
        DateTime from,
        DateTime until,
        CancellationToken cancellationToken = default)
    {
        return await _storage.GetEntriesAsync(from, until, cancellationToken);
    }

    /// <summary>
    /// Executes an audit query.
    /// </summary>
    /// <param name="queryBuilder">The query builder</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Query results</returns>
    public async Task<AuditQueryResult> QueryAsync(
        AuditQueryBuilder queryBuilder,
        CancellationToken cancellationToken = default)
    {
        if (_storage is IAuditQuery queryStorage)
        {
            return await queryStorage.QueryAsync(queryBuilder, cancellationToken);
        }

        // Fallback implementation for storage that doesn't support queries
        var specification = queryBuilder.Build();
        var allEntries = new List<AuditEntry>();

        // This is inefficient for large datasets - storage should implement IAuditQuery
        if (specification.Filters.Any(f => f is TimestampRangeFilter))
        {
            // Extract date range and use it
            var now = DateTime.UtcNow;
            var entries = await _storage.GetEntriesAsync(now.AddDays(-30), now, cancellationToken);
            allEntries.AddRange(entries.Where(e => specification.Filters.All(f => f.Matches(e))));
        }

        return new AuditQueryResult
        {
            Entries = allEntries,
            TotalCount = allEntries.Count,
            ExecutionTimeMs = 0
        };
    }

    /// <summary>
    /// Gets audit statistics for a time period.
    /// </summary>
    /// <param name="from">Start time</param>
    /// <param name="until">End time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Audit statistics</returns>
    public async Task<AuditStatistics> GetStatisticsAsync(
        DateTime from,
        DateTime until,
        CancellationToken cancellationToken = default)
    {
        if (_storage is IAuditQuery queryStorage)
        {
            return await queryStorage.GetStatisticsAsync(from, until, cancellationToken);
        }

        // Fallback implementation
        var entries = await _storage.GetEntriesAsync(from, until, cancellationToken);
        return new AuditStatistics
        {
            TotalEntries = entries.Count,
            CoverageTimeSpan = until - from,
            EntriesByEventType = entries.GroupBy(e => e.EventType).ToDictionary(g => g.Key, g => (long)g.Count())
        };
    }

    /// <summary>
    /// Exports audit entries to a specific format.
    /// </summary>
    /// <param name="from">Start time</param>
    /// <param name="until">End time</param>
    /// <param name="format">Export format</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Exported audit data as string</returns>
    public async Task<string> ExportAuditAsync(
        DateTime from,
        DateTime until,
        AuditExportFormat format,
        CancellationToken cancellationToken = default)
    {
        var entries = await GetAuditEntriesAsync(from, until, cancellationToken);

        return format switch
        {
            AuditExportFormat.Json => System.Text.Json.JsonSerializer.Serialize(entries, _jsonSerializerOptions),
            AuditExportFormat.Csv => ExportToCsv(entries),
            AuditExportFormat.Xml => ExportToXml(entries),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _shutdownTokenSource = new CancellationTokenSource();
        _logger.LogInformation("Pragmatic Audit Service started with storage: {StorageType}", _storage.GetType().Name);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Pragmatic Audit Service");

        _shutdownTokenSource?.Cancel();

        // Final flush before shutdown
        await FlushAuditEntriesAsync(cancellationToken);

        _logger.LogInformation("Pragmatic Audit Service stopped");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            // Cancel and dispose the timer FIRST so the callback cannot fire again
            _flushTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _flushTimer?.Dispose();

            _shutdownTokenSource?.Cancel();
            _shutdownTokenSource?.Dispose();

            // Best-effort synchronous drain: dequeue remaining entries and fire-and-forget storage.
            // Avoid GetAwaiter().GetResult() to prevent deadlocks on synchronization contexts.
            var remaining = new List<AuditEntry>();
            while (_auditQueue.TryDequeue(out var entry))
                remaining.Add(entry);

            if (remaining.Count > 0)
            {
                try
                {
                    // Run on a dedicated thread without synchronization context to avoid deadlock
                    Task.Run(() => _storage.StoreEntriesAsync(remaining, CancellationToken.None))
                        .Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during final audit flush on dispose");
                }
            }

            _storage?.Dispose();
        }
    }

    private void EnqueueEntry(AuditEntry entry, bool forceImmediateFlush = false)
    {
        _auditQueue.Enqueue(entry);

        if (forceImmediateFlush || _policy.ShouldFlushImmediately(entry) || _auditQueue.Count >= _options.BatchSize)
        {
            _ = Task.Run(() => FlushAuditEntriesAsync(_shutdownTokenSource?.Token ?? CancellationToken.None));
        }
    }

    private void FlushAuditEntries(object? state)
    {
        _ = Task.Run(() => FlushAuditEntriesAsync(_shutdownTokenSource?.Token ?? CancellationToken.None));
    }

    private async Task FlushAuditEntriesAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
            return;

        var entries = new List<AuditEntry>();
        while (_auditQueue.TryDequeue(out var entry) && entries.Count < _options.BatchSize)
        {
            entries.Add(entry);
        }

        if (entries.Count > 0)
        {
            try
            {
                await _storage.StoreEntriesAsync(entries, cancellationToken);
                _logger.LogDebug("Flushed {Count} audit entries", entries.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush {Count} audit entries", entries.Count);

                // Re-enqueue for retry, but bound the queue so a persistently unavailable
                // storage backend can't grow it without limit (which would eventually OOM).
                // Once MaxQueueSize is reached, newest re-enqueue attempts are dropped and a
                // diagnostic is surfaced so the data loss is observable.
                var droppedCount = 0;
                foreach (var entry in entries)
                {
                    if (_auditQueue.Count >= _options.MaxQueueSize)
                    {
                        droppedCount++;
                        continue;
                    }

                    _auditQueue.Enqueue(entry);
                }

                if (droppedCount > 0)
                {
                    _logger.LogWarning(
                        "Audit queue at capacity ({MaxQueueSize}); dropped {DroppedCount} entries during retry re-enqueue because storage is unavailable",
                        _options.MaxQueueSize,
                        droppedCount);
                }
            }
        }
    }

    private static AuditSeverity DetermineSeverityFromReason(RedactionReason reason)
    {
        return reason switch
        {
            RedactionReason.ComplianceRequirement => AuditSeverity.High,
            RedactionReason.SecurityPolicy => AuditSeverity.High,
            RedactionReason.MarkedSensitive => AuditSeverity.Medium,
            RedactionReason.PatternMatch => AuditSeverity.Medium,
            RedactionReason.ManualRedaction => AuditSeverity.Low,
            _ => AuditSeverity.Medium
        };
    }

    private static string ExportToCsv(IReadOnlyList<AuditEntry> entries)
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Timestamp,EventType,CategoryName,PropertyName,UserId,ComplianceStandard,RedactionReason,Severity,Description");

        foreach (var entry in entries)
        {
            csv.AppendLine(CultureInfo.InvariantCulture, $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}," +
                          $"{entry.EventType}," +
                          $"{entry.CategoryName}," +
                          $"{entry.PropertyName}," +
                          $"{entry.UserId}," +
                          $"{entry.ComplianceStandard}," +
                          $"{entry.RedactionReason}," +
                          $"{entry.Severity}," +
                          $"\"{entry.Description}\"");
        }

        return csv.ToString();
    }

    private static string ExportToXml(IReadOnlyList<AuditEntry> entries)
    {
        var xml = new System.Text.StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<AuditEntries>");

        foreach (var entry in entries)
        {
            xml.AppendLine("  <Entry>");
            xml.AppendLine(CultureInfo.InvariantCulture, $"    <Timestamp>{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}</Timestamp>");
            xml.AppendLine(CultureInfo.InvariantCulture, $"    <EventType>{entry.EventType}</EventType>");
            xml.AppendLine(CultureInfo.InvariantCulture, $"    <CategoryName>{entry.CategoryName}</CategoryName>");
            xml.AppendLine(CultureInfo.InvariantCulture, $"    <PropertyName>{entry.PropertyName}</PropertyName>");
            xml.AppendLine(CultureInfo.InvariantCulture, $"    <UserId>{entry.UserId}</UserId>");
            xml.AppendLine(CultureInfo.InvariantCulture, $"    <ComplianceStandard>{entry.ComplianceStandard}</ComplianceStandard>");
            xml.AppendLine(CultureInfo.InvariantCulture, $"    <RedactionReason>{entry.RedactionReason}</RedactionReason>");
            xml.AppendLine(CultureInfo.InvariantCulture, $"    <Severity>{entry.Severity}</Severity>");
            xml.AppendLine(CultureInfo.InvariantCulture, $"    <Description>{entry.Description}</Description>");
            xml.AppendLine("  </Entry>");
        }

        xml.AppendLine("</AuditEntries>");
        return xml.ToString();
    }
}

/// <summary>
/// Configuration options for the audit service.
/// </summary>
public sealed class AuditServiceOptions
{
    /// <summary>Gets or sets the batch size for audit entries.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Gets or sets the flush interval for audit entries.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the maximum queue size before dropping entries.</summary>
    public int MaxQueueSize { get; set; } = 10000;

    /// <summary>Gets or sets whether to enable compression for stored entries.</summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>Gets or sets whether to enable deduplication of similar entries.</summary>
    public bool EnableDeduplication { get; set; }
}