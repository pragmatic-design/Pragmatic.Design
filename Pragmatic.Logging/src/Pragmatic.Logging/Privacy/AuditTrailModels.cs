using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Privacy;

public sealed class AuditConfiguration
{
    /// <summary>
    /// Gets or sets whether audit trail is enabled.
    /// </summary>
    public bool EnableAuditTrail { get; set; }

    /// <summary>
    /// Gets or sets the batch size for audit entries.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum number of queued audit entries.
    /// </summary>
    /// <remarks>
    /// When storage is unavailable, failed entries are re-enqueued for retry. This cap bounds
    /// that retry queue so a persistently failing backend cannot grow it without limit. Once
    /// the cap is reached, surplus re-enqueued entries are dropped.
    /// </remarks>
    public int MaxQueueSize { get; set; } = 10000;

    /// <summary>
    /// Gets or sets whether to flush immediately for critical events.
    /// </summary>
    public bool FlushImmediately { get; set; } = true;

    /// <summary>
    /// Gets or sets the audit storage implementation.
    /// </summary>
    public IAuditStorage? AuditStorage { get; set; }
}

/// <summary>
/// Audit entry representing a privacy or compliance event.
/// </summary>
public sealed class AuditEntry
{
    /// <summary>Gets or sets the timestamp of the event.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Gets or sets the type of audit event.</summary>
    public AuditEventType EventType { get; set; }

    /// <summary>Gets or sets the log level.</summary>
    public string? LogLevel { get; set; }

    /// <summary>Gets or sets the logger category name.</summary>
    public string? CategoryName { get; set; }

    /// <summary>Gets or sets the property name that was affected.</summary>
    public string? PropertyName { get; set; }

    /// <summary>Gets or sets the original length of redacted data.</summary>
    public int OriginalLength { get; set; }

    /// <summary>Gets or sets the reason for redaction.</summary>
    public RedactionReason RedactionReason { get; set; }

    /// <summary>Gets or sets the compliance standard.</summary>
    public ComplianceStandard ComplianceStandard { get; set; }

    /// <summary>Gets or sets the user ID involved.</summary>
    public string? UserId { get; set; }

    /// <summary>Gets or sets the correlation ID.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Gets or sets the data type for sensitive data access.</summary>
    public string? DataType { get; set; }

    /// <summary>Gets or sets the access reason.</summary>
    public string? AccessReason { get; set; }

    /// <summary>Gets or sets the violation type.</summary>
    public string? ViolationType { get; set; }

    /// <summary>Gets or sets the description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the severity level.</summary>
    public AuditSeverity Severity { get; set; }

    /// <summary>Gets or sets the thread ID.</summary>
    public int ThreadId { get; set; }

    /// <summary>Gets or sets the process ID.</summary>
    public int ProcessId { get; set; }

    /// <summary>Gets or sets custom properties for this audit entry.</summary>
    public Dictionary<string, object?>? CustomProperties { get; set; }
}

/// <summary>
/// Types of audit events.
/// </summary>
public enum AuditEventType
{
    /// <summary>Data redaction event.</summary>
    DataRedaction,

    /// <summary>Sensitive data access event.</summary>
    SensitiveDataAccess,

    /// <summary>Compliance violation event.</summary>
    ComplianceViolation,

    /// <summary>Configuration change event.</summary>
    ConfigurationChange,

    /// <summary>General data access event.</summary>
    DataAccess,

    /// <summary>Data processing event.</summary>
    DataProcessing,

    /// <summary>Security event.</summary>
    SecurityEvent,

    /// <summary>Network access event.</summary>
    NetworkAccess,

    /// <summary>Data erasure event.</summary>
    DataErasure,

    /// <summary>Data disclosure event.</summary>
    DataDisclosure
}

/// <summary>
/// Reasons for data redaction.
/// </summary>
public enum RedactionReason
{
    /// <summary>Property marked as sensitive.</summary>
    MarkedSensitive,

    /// <summary>Pattern matched sensitive data.</summary>
    PatternMatch,

    /// <summary>Compliance requirement.</summary>
    ComplianceRequirement,

    /// <summary>Manual redaction request.</summary>
    ManualRedaction,

    /// <summary>Security policy.</summary>
    SecurityPolicy
}

/// <summary>
/// Audit event severity levels.
/// </summary>
public enum AuditSeverity
{
    /// <summary>Low severity - informational.</summary>
    Low = 0,

    /// <summary>Medium severity - warning.</summary>
    Medium = 1,

    /// <summary>High severity - error.</summary>
    High = 2,

    /// <summary>Critical severity - critical security event.</summary>
    Critical = 3
}

/// <summary>
/// Audit export formats.
/// </summary>
public enum AuditExportFormat
{
    /// <summary>JSON format.</summary>
    Json,

    /// <summary>CSV format.</summary>
    Csv,

    /// <summary>XML format.</summary>
    Xml
}

/// <summary>
/// Interface for audit storage implementations.
/// </summary>
public interface IAuditStorage
{
    /// <summary>
    /// Stores audit entries.
    /// </summary>
    /// <param name="entries">The entries to store</param>
    /// <returns>Task representing the async operation</returns>
    Task StoreEntriesAsync(IReadOnlyList<AuditEntry> entries);

    /// <summary>
    /// Gets audit entries for a time period.
    /// </summary>
    /// <param name="from">Start time</param>
    /// <param name="until">End time</param>
    /// <returns>List of audit entries</returns>
    Task<IReadOnlyList<AuditEntry>> GetEntriesAsync(DateTime from, DateTime until);
}