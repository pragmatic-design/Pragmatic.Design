namespace Pragmatic.Logging.Privacy;

/// <summary>
/// Configuration options for secret detection engine.
/// </summary>
public sealed class SecretDetectionOptions
{
    /// <summary>Gets or sets the minimum length for a potential secret.</summary>
    public int MinimumSecretLength { get; set; } = 8;

    /// <summary>Gets or sets the maximum length for a potential secret.</summary>
    public int MaximumSecretLength { get; set; } = 2048;

    /// <summary>Gets or sets whether to precompile critical patterns for performance.</summary>
    public bool PrecompilePatterns { get; set; } = true;

    /// <summary>Gets or sets whether to enable multiline regex matching.</summary>
    public bool EnableMultilineMatching { get; set; } = true;

    /// <summary>Gets or sets the maximum number of patterns to check per scan (0 = unlimited).</summary>
    public int MaxPatternsPerScan { get; set; }

    /// <summary>Gets or sets the regex timeout in seconds.</summary>
    public double RegexTimeoutSeconds { get; set; } = 2.0;

    /// <summary>Gets or sets whether to skip known placeholder values.</summary>
    public bool SkipPlaceholders { get; set; } = true;

    /// <summary>Gets or sets whether to skip detected false positives.</summary>
    public bool SkipFalsePositives { get; set; } = true;

    /// <summary>Gets or sets the minimum confidence threshold for redaction (0.0-1.0).</summary>
    public double MinimumConfidenceForRedaction { get; set; } = 0.7;

    /// <summary>Gets or sets whether to record detections in audit trail.</summary>
    public bool EnableAuditTrail { get; set; } = true;

    /// <summary>Gets or sets the redaction style for detected secrets.</summary>
    public SecretRedactionStyle RedactionStyle { get; set; } = SecretRedactionStyle.Placeholder;

    /// <summary>
    /// Creates default configuration optimized for production use.
    /// </summary>
    /// <returns>Default secret detection options</returns>
    public static SecretDetectionOptions CreateDefault()
    {
        return new SecretDetectionOptions
        {
            MinimumSecretLength = 8,
            MaximumSecretLength = 2048,
            PrecompilePatterns = true,
            EnableMultilineMatching = true,
            MaxPatternsPerScan = 50, // Reasonable limit for performance
            RegexTimeoutSeconds = 2.0,
            SkipPlaceholders = true,
            SkipFalsePositives = true,
            MinimumConfidenceForRedaction = 0.7,
            EnableAuditTrail = true,
            RedactionStyle = SecretRedactionStyle.Placeholder
        };
    }

    /// <summary>
    /// Creates configuration optimized for development environments.
    /// More permissive to catch potential issues.
    /// </summary>
    /// <returns>Development-optimized options</returns>
    public static SecretDetectionOptions CreateForDevelopment()
    {
        return new SecretDetectionOptions
        {
            MinimumSecretLength = 6,
            MaximumSecretLength = 4096,
            PrecompilePatterns = false, // Faster startup in dev
            EnableMultilineMatching = true,
            MaxPatternsPerScan = 0, // No limits in dev
            RegexTimeoutSeconds = 5.0,
            SkipPlaceholders = false, // Catch test secrets too
            SkipFalsePositives = false,
            MinimumConfidenceForRedaction = 0.5, // Lower threshold
            EnableAuditTrail = false, // Less noise in dev
            RedactionStyle = SecretRedactionStyle.PreserveStructure
        };
    }

    /// <summary>
    /// Creates configuration optimized for high-performance scenarios.
    /// Focuses on critical secrets only with minimal processing.
    /// </summary>
    /// <returns>Performance-optimized options</returns>
    public static SecretDetectionOptions CreateForHighPerformance()
    {
        return new SecretDetectionOptions
        {
            MinimumSecretLength = 16,
            MaximumSecretLength = 1024,
            PrecompilePatterns = true,
            EnableMultilineMatching = false, // Faster regex
            MaxPatternsPerScan = 20, // Only critical patterns
            RegexTimeoutSeconds = 0.5, // Very short timeout
            SkipPlaceholders = true,
            SkipFalsePositives = true,
            MinimumConfidenceForRedaction = 0.9, // High confidence only
            EnableAuditTrail = true,
            RedactionStyle = SecretRedactionStyle.Minimal // Fastest redaction
        };
    }

    /// <summary>
    /// Creates configuration optimized for security-focused environments.
    /// More aggressive detection with comprehensive patterns.
    /// </summary>
    /// <returns>Security-focused options</returns>
    public static SecretDetectionOptions CreateForSecurity()
    {
        return new SecretDetectionOptions
        {
            MinimumSecretLength = 4,
            MaximumSecretLength = 8192,
            PrecompilePatterns = true,
            EnableMultilineMatching = true,
            MaxPatternsPerScan = 0, // Use all patterns
            RegexTimeoutSeconds = 10.0, // Allow complex patterns
            SkipPlaceholders = false, // Don't skip anything
            SkipFalsePositives = false,
            MinimumConfidenceForRedaction = 0.3, // Very low threshold
            EnableAuditTrail = true,
            RedactionStyle = SecretRedactionStyle.PreserveLength
        };
    }
}

/// <summary>
/// Styles for redacting detected secrets.
/// </summary>
public enum SecretRedactionStyle
{
    /// <summary>Replace with descriptive placeholder like [API_KEY_REDACTED].</summary>
    Placeholder = 0,

    /// <summary>Replace with asterisks preserving original length.</summary>
    PreserveLength = 1,

    /// <summary>Replace with structured placeholder preserving format (JWT, etc.).</summary>
    PreserveStructure = 2,

    /// <summary>Replace with simple [REDACTED] text.</summary>
    Minimal = 3
}