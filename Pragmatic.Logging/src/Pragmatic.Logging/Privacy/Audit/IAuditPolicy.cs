using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Privacy.Audit;

/// <summary>
/// Interface for audit policies that control audit behavior.
/// </summary>
public interface IAuditPolicy
{
    /// <summary>
    /// Determines whether an audit entry should be recorded based on the context.
    /// </summary>
    /// <param name="context">The audit context</param>
    /// <returns>True if the entry should be audited</returns>
    bool ShouldAudit(AuditContext context);

    /// <summary>
    /// Transforms an audit entry before it is stored.
    /// </summary>
    /// <param name="entry">The original audit entry</param>
    /// <returns>The transformed audit entry</returns>
    AuditEntry TransformEntry(AuditEntry entry);

    /// <summary>
    /// Determines whether an audit entry should be flushed immediately rather than batched.
    /// </summary>
    /// <param name="entry">The audit entry</param>
    /// <returns>True if immediate flush is required</returns>
    bool ShouldFlushImmediately(AuditEntry entry);

    /// <summary>
    /// Gets the retention period for audit entries.
    /// </summary>
    /// <param name="entry">The audit entry</param>
    /// <returns>Retention period, or null for default retention</returns>
    TimeSpan? GetRetentionPeriod(AuditEntry entry);
}

/// <summary>
/// Context information for audit decisions.
/// </summary>
public sealed class AuditContext
{
    /// <summary>Gets or sets the event type being audited.</summary>
    public AuditEventType EventType { get; set; }

    /// <summary>Gets or sets the compliance standard.</summary>
    public ComplianceStandard ComplianceStandard { get; set; }

    /// <summary>Gets or sets the severity level.</summary>
    public AuditSeverity Severity { get; set; }

    /// <summary>Gets or sets the log level that triggered the audit.</summary>
    public Microsoft.Extensions.Logging.LogLevel LogLevel { get; set; }

    /// <summary>Gets or sets the logger category name.</summary>
    public string? CategoryName { get; set; }

    /// <summary>Gets or sets the user ID if available.</summary>
    public string? UserId { get; set; }

    /// <summary>Gets or sets whether this is a critical security event.</summary>
    public bool IsCriticalSecurityEvent { get; set; }

    /// <summary>Gets or sets additional context properties.</summary>
    public Dictionary<string, object?> Properties { get; set; } = new();
}

/// <summary>
/// Default audit policy implementation.
/// </summary>
public sealed class DefaultAuditPolicy(AuditPolicyOptions? options = null) : IAuditPolicy
{
    private readonly AuditPolicyOptions _options = options ?? new AuditPolicyOptions();

    public bool ShouldAudit(AuditContext context)
    {
        // Always audit compliance violations and critical events
        if (context.EventType == AuditEventType.ComplianceViolation || context.IsCriticalSecurityEvent)
            return true;

        // Check minimum severity
        if (context.Severity < _options.MinimumSeverity)
            return false;

        // Check excluded categories
        if (context.CategoryName != null &&
            _options.ExcludedCategories.Any(pattern => CategoryMatches(context.CategoryName, pattern)))
            return false;

        return true;
    }

    public AuditEntry TransformEntry(AuditEntry entry)
    {
        // Add standard metadata
        entry.CustomProperties ??= new Dictionary<string, object?>();
        entry.CustomProperties["MachineName"] = Environment.MachineName;
        entry.CustomProperties["ProcessName"] = Environment.ProcessPath;
        entry.CustomProperties["AuditPolicyVersion"] = _options.PolicyVersion;

        return entry;
    }

    public bool ShouldFlushImmediately(AuditEntry entry)
    {
        return entry.EventType == AuditEventType.ComplianceViolation ||
               entry.Severity >= AuditSeverity.Critical ||
               _options.ImmediateFlushEventTypes.Contains(entry.EventType);
    }

    public TimeSpan? GetRetentionPeriod(AuditEntry entry)
    {
        return entry.ComplianceStandard switch
        {
            ComplianceStandard.Gdpr => TimeSpan.FromDays(2555), // 7 years for legal claims
            ComplianceStandard.Hipaa => TimeSpan.FromDays(2190), // 6 years
            ComplianceStandard.PciDss => TimeSpan.FromDays(365), // 1 year minimum
            _ => _options.DefaultRetentionPeriod
        };
    }

    private static bool CategoryMatches(string categoryName, string pattern)
    {
        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            var regex = new System.Text.RegularExpressions.Regex(
                "^" + pattern.Replace("*", ".*").Replace("?", ".") + "$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return regex.IsMatch(categoryName);
        }

        return categoryName.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Options for audit policy configuration.
/// </summary>
public sealed class AuditPolicyOptions
{
    /// <summary>Gets or sets the minimum severity level to audit.</summary>
    public AuditSeverity MinimumSeverity { get; set; } = AuditSeverity.Low;

    /// <summary>Gets or sets category patterns to exclude from auditing.</summary>
    public string[] ExcludedCategories { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets event types that should be flushed immediately.</summary>
    public HashSet<AuditEventType> ImmediateFlushEventTypes { get; set; } = new()
    {
        AuditEventType.ComplianceViolation
    };

    /// <summary>Gets or sets the default retention period for audit entries.</summary>
    public TimeSpan DefaultRetentionPeriod { get; set; } = TimeSpan.FromDays(90);

    /// <summary>Gets or sets the policy version for tracking.</summary>
    public string PolicyVersion { get; set; } = "1.0";
}

/// <summary>
/// GDPR-specific audit policy.
/// </summary>
public sealed class GdprAuditPolicy : IAuditPolicy
{
    public bool ShouldAudit(AuditContext context)
    {
        // Audit all GDPR-related events
        return context.ComplianceStandard == ComplianceStandard.Gdpr ||
               context.EventType == AuditEventType.DataRedaction ||
               context.EventType == AuditEventType.SensitiveDataAccess;
    }

    public AuditEntry TransformEntry(AuditEntry entry)
    {
        // Add GDPR-specific metadata
        entry.CustomProperties ??= new Dictionary<string, object?>();
        entry.CustomProperties["GdprLegalBasis"] = DetermineLegalBasis(entry);
        entry.CustomProperties["DataProcessingPurpose"] = DetermineProcessingPurpose(entry);
        entry.CustomProperties["DataRetentionCategory"] = DetermineRetentionCategory(entry);
        entry.CustomProperties["GdprArticle"] = DetermineRelevantArticle(entry);

        return entry;
    }

    public bool ShouldFlushImmediately(AuditEntry entry)
    {
        // GDPR requires immediate auditing for high-risk processing
        return entry.EventType == AuditEventType.ComplianceViolation ||
               entry.Severity >= AuditSeverity.High ||
               entry.ComplianceStandard == ComplianceStandard.Gdpr;
    }

    public TimeSpan? GetRetentionPeriod(AuditEntry entry)
    {
        // GDPR audit logs should be kept for potential legal claims
        return TimeSpan.FromDays(2555); // 7 years
    }

    private static string DetermineLegalBasis(AuditEntry entry)
    {
        return entry.EventType switch
        {
            AuditEventType.DataRedaction => "Article6(1)(c) - Legal obligation",
            AuditEventType.SensitiveDataAccess => "Article6(1)(b) - Contract performance",
            AuditEventType.ComplianceViolation => "Article6(1)(c) - Legal obligation",
            _ => "Article6(1)(f) - Legitimate interests"
        };
    }

    private static string DetermineProcessingPurpose(AuditEntry entry)
    {
        return entry.EventType switch
        {
            AuditEventType.DataRedaction => "Privacy protection and compliance",
            AuditEventType.SensitiveDataAccess => "Service provision and support",
            AuditEventType.ComplianceViolation => "Compliance monitoring and breach detection",
            _ => "System monitoring and security"
        };
    }

    private static string DetermineRetentionCategory(AuditEntry entry)
    {
        return entry.EventType switch
        {
            AuditEventType.DataRedaction => "ComplianceLogs",
            AuditEventType.SensitiveDataAccess => "AccessLogs",
            AuditEventType.ComplianceViolation => "SecurityLogs",
            _ => "SystemLogs"
        };
    }

    private static string DetermineRelevantArticle(AuditEntry entry)
    {
        return entry.EventType switch
        {
            AuditEventType.DataRedaction => "Article 32 - Security of processing",
            AuditEventType.SensitiveDataAccess => "Article 30 - Records of processing",
            AuditEventType.ComplianceViolation => "Article 33 - Breach notification",
            _ => "Article 25 - Data protection by design"
        };
    }
}

/// <summary>
/// Performance-focused audit policy that only audits critical events.
/// </summary>
public sealed class PerformanceAuditPolicy : IAuditPolicy
{
    public bool ShouldAudit(AuditContext context)
    {
        // Only audit high-severity events and compliance violations
        return context.Severity >= AuditSeverity.High ||
               context.EventType == AuditEventType.ComplianceViolation ||
               context.IsCriticalSecurityEvent;
    }

    public AuditEntry TransformEntry(AuditEntry entry)
    {
        // Minimal transformation for performance
        return entry;
    }

    public bool ShouldFlushImmediately(AuditEntry entry)
    {
        // Immediate flush only for critical violations
        return entry.EventType == AuditEventType.ComplianceViolation;
    }

    public TimeSpan? GetRetentionPeriod(AuditEntry entry)
    {
        // Shorter retention for performance environments
        return TimeSpan.FromDays(30);
    }
}
