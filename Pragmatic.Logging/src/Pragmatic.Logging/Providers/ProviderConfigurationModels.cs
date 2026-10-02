using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Providers;

/// <summary>
/// Configuration for context property filtering.
/// </summary>
public sealed class ContextFilterConfiguration
{
    /// <summary>
    /// Gets or sets the filter mode (Include, Exclude, or All).
    /// </summary>
    public ContextFilterMode Mode { get; set; } = ContextFilterMode.All;

    /// <summary>
    /// Gets or sets the list of property names to include/exclude based on Mode.
    /// </summary>
    public HashSet<string> PropertyNames { get; set; } = new();

    /// <summary>
    /// Gets or sets patterns for property name matching (supports wildcards).
    /// </summary>
    public HashSet<string> PropertyPatterns { get; set; } = new();

    /// <summary>
    /// Gets or sets whether to include sensitive properties marked as redacted.
    /// </summary>
    public bool IncludeRedactedProperties { get; set; }

    /// <summary>
    /// Gets or sets the maximum depth for nested object properties.
    /// </summary>
    public int MaxDepth { get; set; } = 3;
}

/// <summary>
/// Context filter modes.
/// </summary>
public enum ContextFilterMode
{
    /// <summary>Include all context properties.</summary>
    All,
    /// <summary>Include only specified properties.</summary>
    Include,
    /// <summary>Exclude specified properties.</summary>
    Exclude,
    /// <summary>Include no properties.</summary>
    None
}

/// <summary>
/// Configuration for message formatting.
/// </summary>
public sealed class FormattingConfiguration
{
    /// <summary>
    /// Gets or sets the timestamp format.
    /// </summary>
    public string TimestampFormat { get; set; } = "yyyy-MM-dd HH:mm:ss.fff";

    /// <summary>
    /// Gets or sets whether to use UTC timestamps.
    /// </summary>
    public bool UseUtcTimestamp { get; set; } = true;

    /// <summary>
    /// Gets or sets the message template format.
    /// </summary>
    public string MessageTemplate { get; set; } = "[{Timestamp}] [{Level}] {Category}: {Message}";

    /// <summary>
    /// Gets or sets whether to include exception details.
    /// </summary>
    public bool IncludeExceptionDetails { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum length for log messages (0 = no limit).
    /// </summary>
    public int MaxMessageLength { get; set; }

    /// <summary>
    /// Gets or sets custom formatters for specific property types.
    /// </summary>
    public Dictionary<Type, Func<object, string>> CustomFormatters { get; set; } = new();

    /// <summary>
    /// Gets or sets whether to pretty-print JSON properties.
    /// </summary>
    public bool PrettyPrintJson { get; set; }
}

/// <summary>
/// Configuration for performance and batching.
/// </summary>
public sealed class PerformanceConfiguration
{
    /// <summary>
    /// Gets or sets whether to enable batching for better performance.
    /// </summary>
    public bool EnableBatching { get; set; } = true;

    /// <summary>
    /// Gets or sets the batch size for batched providers.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the flush interval for batched providers.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the maximum queue size before dropping messages.
    /// </summary>
    public int MaxQueueSize { get; set; } = 10000;

    /// <summary>
    /// Gets or sets the queue overflow strategy.
    /// </summary>
    public QueueOverflowStrategy OverflowStrategy { get; set; } = QueueOverflowStrategy.DropOldest;

    /// <summary>
    /// Gets or sets whether to use zero-allocation patterns where possible.
    /// </summary>
    public bool UseZeroAllocation { get; set; } = true;

    /// <summary>
    /// Gets or sets the background thread priority for batched providers.
    /// </summary>
    public System.Threading.ThreadPriority BackgroundThreadPriority { get; set; } = System.Threading.ThreadPriority.BelowNormal;
}

/// <summary>
/// Queue overflow strategies for when the log queue is full.
/// </summary>
public enum QueueOverflowStrategy
{
    /// <summary>Drop the oldest messages to make room for new ones.</summary>
    DropOldest,
    /// <summary>Drop the newest messages when queue is full.</summary>
    DropNewest,
    /// <summary>Block until space is available (may impact performance).</summary>
    Block,
    /// <summary>Expand the queue size dynamically.</summary>
    Expand
}

/// <summary>
/// Provider metrics for monitoring and diagnostics.
/// </summary>
public sealed class ProviderMetrics
{
    /// <summary>Gets or sets the total number of messages processed.</summary>
    public long TotalMessages { get; set; }

    /// <summary>Gets or sets the number of messages dropped due to overflow.</summary>
    public long DroppedMessages { get; set; }

    /// <summary>Gets or sets the number of failed message attempts.</summary>
    public long FailedMessages { get; set; }

    /// <summary>Gets or sets the current queue size.</summary>
    public int CurrentQueueSize { get; set; }

    /// <summary>Gets or sets the peak queue size reached.</summary>
    public int PeakQueueSize { get; set; }

    /// <summary>Gets or sets the average message processing time in milliseconds.</summary>
    public double AverageProcessingTimeMs { get; set; }

    /// <summary>Gets or sets the last error that occurred.</summary>
    public string? LastError { get; set; }

    /// <summary>Gets or sets the timestamp of the last error.</summary>
    public DateTime? LastErrorTime { get; set; }

    /// <summary>Gets or sets provider-specific metrics.</summary>
    public Dictionary<string, object?> CustomMetrics { get; set; } = new();
}

/// <summary>
/// Health status for provider monitoring.
/// </summary>
public enum ProviderHealthStatus
{
    /// <summary>Provider is healthy and functioning normally.</summary>
    Healthy,
    /// <summary>Provider is functioning but with warnings.</summary>
    Warning,
    /// <summary>Provider has errors but is still partially functional.</summary>
    Degraded,
    /// <summary>Provider is not functioning.</summary>
    Unhealthy
}

/// <summary>
/// Configuration for privacy and data redaction features.
/// </summary>
public sealed class PrivacyConfiguration
{
    /// <summary>
    /// Gets or sets whether automatic data redaction is enabled.
    /// </summary>
    public bool EnableRedaction { get; set; }

    /// <summary>
    /// Gets or sets the redaction mode to use.
    /// </summary>
    public RedactionMode RedactionMode { get; set; } = RedactionMode.Conservative;

    /// <summary>
    /// Gets or sets custom sensitive property names that should always be redacted.
    /// </summary>
    public string[] SensitivePropertyNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets regex patterns for property names that should be redacted.
    /// </summary>
    public string[] PropertyNamePatterns { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets regex patterns for redacting content in log messages.
    /// </summary>
    public string[] MessageRedactionPatterns { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the placeholder text used for redacted values.
    /// </summary>
    public string RedactionPlaceholder { get; set; } = "[REDACTED]";

    /// <summary>
    /// Gets or sets whether to preserve the length of redacted strings.
    /// </summary>
    public bool PreserveLengths { get; set; }

    /// <summary>
    /// Gets or sets whether to preserve JSON structure when redacting.
    /// </summary>
    public bool PreserveJsonStructure { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to perform deep redaction on complex objects.
    /// </summary>
    public bool EnableDeepRedaction { get; set; }

    /// <summary>
    /// Gets or sets whether to enable audit trail for sensitive data access.
    /// </summary>
    public bool EnableAuditTrail { get; set; }

    /// <summary>
    /// Gets or sets the primary compliance standard being followed.
    /// </summary>
    /// <remarks>
    /// <see cref="ComplianceStandard"/> is not a <c>[Flags]</c> enum, so it can only hold a
    /// single value. When more than one standard applies (multi-compliance), this holds the
    /// most restrictive primary standard and <see cref="AdditionalComplianceStandards"/>
    /// carries the remaining ones.
    /// </remarks>
    public ComplianceStandard ComplianceStandard { get; set; } = ComplianceStandard.General;

    /// <summary>
    /// Gets or sets any additional compliance standards applied beyond
    /// <see cref="ComplianceStandard"/> when multiple standards are combined.
    /// </summary>
    public IReadOnlyList<ComplianceStandard> AdditionalComplianceStandards { get; set; } = Array.Empty<ComplianceStandard>();

    /// <summary>
    /// Gets or sets whether explicit consent is required for data processing.
    /// </summary>
    public bool RequireExplicitConsent { get; set; }

    /// <summary>
    /// Gets or sets the data retention period for compliance.
    /// </summary>
    public TimeSpan DataRetentionPeriod { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Creates a deep copy of this privacy configuration, including its array/list members.
    /// </summary>
    public PrivacyConfiguration Clone() => new()
    {
        EnableRedaction = EnableRedaction,
        RedactionMode = RedactionMode,
        SensitivePropertyNames = (string[])SensitivePropertyNames.Clone(),
        PropertyNamePatterns = (string[])PropertyNamePatterns.Clone(),
        MessageRedactionPatterns = (string[])MessageRedactionPatterns.Clone(),
        RedactionPlaceholder = RedactionPlaceholder,
        PreserveLengths = PreserveLengths,
        PreserveJsonStructure = PreserveJsonStructure,
        EnableDeepRedaction = EnableDeepRedaction,
        EnableAuditTrail = EnableAuditTrail,
        ComplianceStandard = ComplianceStandard,
        AdditionalComplianceStandards = AdditionalComplianceStandards.ToArray(),
        RequireExplicitConsent = RequireExplicitConsent,
        DataRetentionPeriod = DataRetentionPeriod
    };
}

/// <summary>
/// Redaction mode for privacy configuration.
/// </summary>
public enum RedactionMode
{
    /// <summary>
    /// No redaction applied.
    /// </summary>
    None = 0,

    /// <summary>
    /// Conservative redaction - only explicitly marked sensitive data.
    /// </summary>
    Conservative = 1,

    /// <summary>
    /// Standard redaction - common sensitive patterns.
    /// </summary>
    Standard = 2,

    /// <summary>
    /// Aggressive redaction - GDPR compliant with extensive patterns.
    /// </summary>
    Aggressive = 3,

    /// <summary>
    /// Custom redaction - user-defined patterns only.
    /// </summary>
    Custom = 4
}

/// <summary>
/// Compliance standards supported by the privacy system.
/// </summary>
public enum ComplianceStandard
{
    /// <summary>General privacy protection without specific compliance.</summary>
    General = 0,

    /// <summary>Development environment with minimal restrictions.</summary>
    Development = 1,

    /// <summary>General Data Protection Regulation (EU).</summary>
    Gdpr = 2,

    /// <summary>Health Insurance Portability and Accountability Act (US Healthcare).</summary>
    Hipaa = 3,

    /// <summary>Payment Card Industry Data Security Standard.</summary>
    PciDss = 4,

    /// <summary>California Consumer Privacy Act.</summary>
    Ccpa = 5,

    /// <summary>SOX compliance for financial reporting.</summary>
    Sox = 6
}