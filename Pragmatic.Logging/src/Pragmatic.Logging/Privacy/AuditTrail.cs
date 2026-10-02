using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Privacy;

/// <summary>
/// Audit trail system for tracking sensitive data access and redaction events.
/// </summary>
public sealed class AuditTrail : IDisposable
{
    private static readonly Lazy<AuditTrail> _instance = new(() => new AuditTrail());
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly ConcurrentQueue<AuditEntry> _auditQueue = new();
    private readonly Timer _flushTimer;
    private volatile bool _disposed;

    /// <summary>
    /// Gets the singleton instance of the audit trail.
    /// </summary>
    public static AuditTrail Instance => _instance.Value;

    /// <summary>
    /// Gets or sets the audit configuration.
    /// </summary>
    public AuditConfiguration Configuration { get; set; } = new();

    private AuditTrail()
    {
        _flushTimer = new Timer(FlushAuditEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        if (_disposed || !Configuration.EnableAuditTrail)
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
            ProcessId = Environment.ProcessId
        };

        _auditQueue.Enqueue(entry);

        // Trigger immediate flush for critical events
        if (Configuration.FlushImmediately && IsCriticalEvent(entry))
        {
            FlushAuditEntries(null);
        }
    }

    /// <summary>
    /// Records a sensitive data access event.
    /// </summary>
    /// <param name="userId">The user accessing the data</param>
    /// <param name="dataType">The type of sensitive data accessed</param>
    /// <param name="accessReason">The reason for access</param>
    /// <param name="correlationId">The correlation ID</param>
    public void RecordSensitiveDataAccess(
        string userId,
        string dataType,
        string accessReason,
        string? correlationId = null)
    {
        if (_disposed || !Configuration.EnableAuditTrail)
            return;

        var entry = new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.SensitiveDataAccess,
            UserId = userId,
            DataType = dataType,
            AccessReason = accessReason,
            CorrelationId = correlationId,
            ThreadId = Environment.CurrentManagedThreadId,
            ProcessId = Environment.ProcessId
        };

        _auditQueue.Enqueue(entry);
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
        if (_disposed || !Configuration.EnableAuditTrail)
            return;

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

        _auditQueue.Enqueue(entry);

        // Always flush immediately for compliance violations
        FlushAuditEntries(null);
    }

    /// <summary>
    /// Records a data access audit event.
    /// </summary>
    /// <param name="eventType">Type of data access event</param>
    /// <param name="entityId">ID of the accessed entity</param>
    /// <param name="complianceStandard">Applicable compliance standard</param>
    /// <param name="description">Description of the access event</param>
    /// <param name="userId">User ID performing the access</param>
    /// <param name="correlationId">Correlation ID for tracking</param>
    public void LogDataAccess(
        string eventType,
        string entityId,
        ComplianceStandard complianceStandard,
        string description,
        string? userId = null,
        string? correlationId = null)
    {
        if (_disposed || !Configuration.EnableAuditTrail)
            return;

        var entry = new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.DataAccess,
            ViolationType = eventType,
            Description = description,
            ComplianceStandard = complianceStandard,
            Severity = AuditSeverity.Medium,
            UserId = userId,
            CorrelationId = correlationId,
            ThreadId = Environment.CurrentManagedThreadId,
            ProcessId = Environment.ProcessId
        };

        entry.CustomProperties ??= new Dictionary<string, object?>();
        entry.CustomProperties["EntityId"] = entityId;

        _auditQueue.Enqueue(entry);

        // Flush for high-sensitivity data access
        FlushAuditEntries(null);
    }

    /// <summary>
    /// Records a data processing audit event.
    /// </summary>
    /// <param name="eventType">Type of data processing event</param>
    /// <param name="entityId">ID of the processed entity</param>
    /// <param name="complianceStandard">Applicable compliance standard</param>
    /// <param name="description">Description of the processing event</param>
    /// <param name="userId">User ID performing the processing</param>
    /// <param name="correlationId">Correlation ID for tracking</param>
    public void LogDataProcessing(
        string eventType,
        string entityId,
        ComplianceStandard complianceStandard,
        string description,
        string? userId = null,
        string? correlationId = null)
    {
        if (_disposed || !Configuration.EnableAuditTrail)
            return;

        var entry = new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.DataProcessing,
            ViolationType = eventType,
            Description = description,
            ComplianceStandard = complianceStandard,
            Severity = AuditSeverity.Medium,
            UserId = userId,
            CorrelationId = correlationId,
            ThreadId = Environment.CurrentManagedThreadId,
            ProcessId = Environment.ProcessId
        };

        entry.CustomProperties ??= new Dictionary<string, object?>();
        entry.CustomProperties["EntityId"] = entityId;

        _auditQueue.Enqueue(entry);

        // Flush for data processing events
        FlushAuditEntries(null);
    }

    /// <summary>
    /// Records a security event audit.
    /// </summary>
    /// <param name="eventType">Type of security event</param>
    /// <param name="entityId">ID of the affected entity</param>
    /// <param name="complianceStandard">Applicable compliance standard</param>
    /// <param name="description">Description of the security event</param>
    /// <param name="userId">User ID associated with the event</param>
    /// <param name="correlationId">Correlation ID for tracking</param>
    public void LogSecurityEvent(
        string eventType,
        string entityId,
        ComplianceStandard complianceStandard,
        string description,
        string? userId = null,
        string? correlationId = null)
    {
        if (_disposed || !Configuration.EnableAuditTrail)
            return;

        var entry = new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.SecurityEvent,
            ViolationType = eventType,
            Description = description,
            ComplianceStandard = complianceStandard,
            Severity = AuditSeverity.High,
            UserId = userId,
            CorrelationId = correlationId,
            ThreadId = Environment.CurrentManagedThreadId,
            ProcessId = Environment.ProcessId
        };

        entry.CustomProperties ??= new Dictionary<string, object?>();
        entry.CustomProperties["EntityId"] = entityId;

        _auditQueue.Enqueue(entry);

        // Always flush immediately for security events
        FlushAuditEntries(null);
    }

    /// <summary>
    /// Records a network access audit event.
    /// </summary>
    /// <param name="eventType">Type of network access event</param>
    /// <param name="endpoint">Network endpoint or resource accessed</param>
    /// <param name="complianceStandard">Applicable compliance standard</param>
    /// <param name="description">Description of the network access</param>
    /// <param name="userId">User ID performing the access</param>
    /// <param name="correlationId">Correlation ID for tracking</param>
    public void LogNetworkAccess(
        string eventType,
        string endpoint,
        ComplianceStandard complianceStandard,
        string description,
        string? userId = null,
        string? correlationId = null)
    {
        if (_disposed || !Configuration.EnableAuditTrail)
            return;

        var entry = new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.NetworkAccess,
            ViolationType = eventType,
            Description = description,
            ComplianceStandard = complianceStandard,
            Severity = AuditSeverity.Medium,
            UserId = userId,
            CorrelationId = correlationId,
            ThreadId = Environment.CurrentManagedThreadId,
            ProcessId = Environment.ProcessId
        };

        entry.CustomProperties ??= new Dictionary<string, object?>();
        entry.CustomProperties["NetworkEndpoint"] = endpoint;

        _auditQueue.Enqueue(entry);

        // Flush for network access events
        FlushAuditEntries(null);
    }

    /// <summary>
    /// Records a data disclosure audit event.
    /// </summary>
    /// <param name="eventType">Type of data disclosure event</param>
    /// <param name="entityId">ID of the disclosed entity</param>
    /// <param name="complianceStandard">Applicable compliance standard</param>
    /// <param name="description">Description of the data disclosure</param>
    /// <param name="userId">User ID responsible for the disclosure</param>
    /// <param name="correlationId">Correlation ID for tracking</param>
    public void LogDataDisclosure(
        string eventType,
        string entityId,
        ComplianceStandard complianceStandard,
        string description,
        string? userId = null,
        string? correlationId = null)
    {
        if (_disposed || !Configuration.EnableAuditTrail)
            return;

        var entry = new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.DataDisclosure,
            ViolationType = eventType,
            Description = description,
            ComplianceStandard = complianceStandard,
            Severity = AuditSeverity.High,
            UserId = userId,
            CorrelationId = correlationId,
            ThreadId = Environment.CurrentManagedThreadId,
            ProcessId = Environment.ProcessId
        };

        entry.CustomProperties ??= new Dictionary<string, object?>();
        entry.CustomProperties["EntityId"] = entityId;

        _auditQueue.Enqueue(entry);

        // Always flush immediately for data disclosure events
        FlushAuditEntries(null);
    }

    /// <summary>
    /// Records a data erasure audit event.
    /// </summary>
    /// <param name="eventType">Type of data erasure event</param>
    /// <param name="entityId">ID of the erased entity</param>
    /// <param name="complianceStandard">Applicable compliance standard</param>
    /// <param name="description">Description of the data erasure</param>
    /// <param name="userId">User ID responsible for the erasure</param>
    /// <param name="correlationId">Correlation ID for tracking</param>
    public void LogDataErasure(
        string eventType,
        string entityId,
        ComplianceStandard complianceStandard,
        string description,
        string? userId = null,
        string? correlationId = null)
    {
        if (_disposed || !Configuration.EnableAuditTrail)
            return;

        var entry = new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.DataErasure,
            ViolationType = eventType,
            Description = description,
            ComplianceStandard = complianceStandard,
            Severity = AuditSeverity.High,
            UserId = userId,
            CorrelationId = correlationId,
            ThreadId = Environment.CurrentManagedThreadId,
            ProcessId = Environment.ProcessId
        };

        entry.CustomProperties ??= new Dictionary<string, object?>();
        entry.CustomProperties["EntityId"] = entityId;

        _auditQueue.Enqueue(entry);

        // Always flush immediately for data erasure events
        FlushAuditEntries(null);
    }

    /// <summary>
    /// Gets audit entries for a specific time period.
    /// </summary>
    /// <param name="from">Start time</param>
    /// <param name="to">End time</param>
    /// <returns>List of audit entries</returns>
    public async Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(DateTime from, DateTime to)
    {
        if (Configuration.AuditStorage == null)
            return Array.Empty<AuditEntry>();

        return await Configuration.AuditStorage.GetEntriesAsync(from, to);
    }

    /// <summary>
    /// Exports audit entries to a specific format.
    /// </summary>
    /// <param name="from">Start time</param>
    /// <param name="to">End time</param>
    /// <param name="format">Export format</param>
    /// <returns>Exported audit data as string</returns>
    public async Task<string> ExportAuditAsync(DateTime from, DateTime to, AuditExportFormat format)
    {
        var entries = await GetAuditEntriesAsync(from, to);

        return format switch
        {
            AuditExportFormat.Json => JsonSerializer.Serialize(entries, _jsonSerializerOptions),
            AuditExportFormat.Csv => ExportToCsv(entries),
            AuditExportFormat.Xml => ExportToXml(entries),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private void FlushAuditEntries(object? state)
    {
        if (_disposed || Configuration.AuditStorage == null)
            return;

        var entries = new List<AuditEntry>();
        while (_auditQueue.TryDequeue(out var entry) && entries.Count < Configuration.BatchSize)
        {
            entries.Add(entry);
        }

        if (entries.Count > 0)
        {
            // Run on a background thread so we can await without blocking the timer thread
            _ = Task.Run(async () =>
            {
                try
                {
                    await Configuration.AuditStorage.StoreEntriesAsync(entries).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Log to stderr; re-enqueue entries for next flush attempt, but bound the
                    // queue so a persistently failing storage backend can't grow it without
                    // limit (eventual OOM). Surplus entries beyond MaxQueueSize are dropped and
                    // the loss is surfaced on stderr.
                    Console.Error.WriteLine($"[AuditTrail] Failed to store {entries.Count} entries: {ex.Message}");

                    var maxQueueSize = Configuration.MaxQueueSize;
                    var dropped = 0;
                    foreach (var e in entries)
                    {
                        if (_auditQueue.Count >= maxQueueSize)
                        {
                            dropped++;
                            continue;
                        }

                        _auditQueue.Enqueue(e);
                    }

                    if (dropped > 0)
                    {
                        Console.Error.WriteLine(
                            $"[AuditTrail] Queue at capacity ({maxQueueSize}); dropped {dropped} entries during retry re-enqueue because storage is unavailable");
                    }
                }
            });
        }
    }

    private static bool IsCriticalEvent(AuditEntry entry)
    {
        return entry.EventType == AuditEventType.ComplianceViolation ||
               entry.Severity >= AuditSeverity.High ||
               entry.ComplianceStandard == ComplianceStandard.Hipaa;
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
        // Use XmlWriter to properly escape user-supplied values and prevent XML injection
        using var stringWriter = new System.IO.StringWriter(CultureInfo.InvariantCulture);
        using var xmlWriter = System.Xml.XmlWriter.Create(stringWriter, new System.Xml.XmlWriterSettings
        {
            Indent = true,
            Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        });

        xmlWriter.WriteStartDocument();
        xmlWriter.WriteStartElement("AuditEntries");

        foreach (var entry in entries)
        {
            xmlWriter.WriteStartElement("Entry");
            xmlWriter.WriteElementString("Timestamp", entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            xmlWriter.WriteElementString("EventType", entry.EventType.ToString());
            xmlWriter.WriteElementString("CategoryName", entry.CategoryName ?? string.Empty);
            xmlWriter.WriteElementString("PropertyName", entry.PropertyName ?? string.Empty);
            xmlWriter.WriteElementString("UserId", entry.UserId ?? string.Empty);
            xmlWriter.WriteElementString("ComplianceStandard", entry.ComplianceStandard.ToString());
            xmlWriter.WriteElementString("RedactionReason", entry.RedactionReason.ToString());
            xmlWriter.WriteElementString("Severity", entry.Severity.ToString());
            xmlWriter.WriteElementString("Description", entry.Description ?? string.Empty);
            xmlWriter.WriteEndElement();
        }

        xmlWriter.WriteEndElement();
        xmlWriter.WriteEndDocument();
        xmlWriter.Flush();

        return stringWriter.ToString();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _flushTimer?.Dispose();
            FlushAuditEntries(null); // Final flush
        }
    }
}
