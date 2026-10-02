namespace Pragmatic.Logging.Privacy;

/// <summary>
/// Internal pattern information for secret detection.
/// </summary>
internal sealed class SecretPatternInfo
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public SecretSeverity Severity { get; set; }
    public double Confidence { get; set; }
}

/// <summary>
/// Context information to improve secret detection accuracy.
/// </summary>
public sealed class SecretDetectionContext
{
    /// <summary>Gets or sets the property name if scanning a property value.</summary>
    public string? PropertyName { get; set; }

    /// <summary>Gets or sets whether this is a property value scan.</summary>
    public bool IsPropertyValue { get; set; }

    /// <summary>Gets or sets whether the property name indicates a secret.</summary>
    public bool PropertyNameIndicatesSecret { get; set; }

    /// <summary>Gets or sets additional confidence boost for this scan.</summary>
    public double ConfidenceBoost { get; set; }

    /// <summary>Gets or sets whether to only use critical patterns for performance.</summary>
    public bool OnlyCriticalPatterns { get; set; }

    /// <summary>Gets or sets the correlation ID for audit purposes.</summary>
    public string? CorrelationId { get; set; }
}

/// <summary>
/// Statistics about secret detection performance and results.
/// </summary>
public sealed class SecretDetectionStatistics
{
    /// <summary>Gets or sets the number of compiled patterns in cache.</summary>
    public int CompiledPatternsCount { get; set; }

    /// <summary>Gets or sets the total number of patterns available.</summary>
    public int TotalPatternsAvailable { get; set; }

    /// <summary>Gets or sets whether patterns are pre-compiled.</summary>
    public bool PrecompiledPatterns { get; set; }

    /// <summary>Gets or sets the current cache size.</summary>
    public int CacheSize { get; set; }

    /// <summary>Gets or sets the detection options being used.</summary>
    public SecretDetectionOptions? Options { get; set; }
}