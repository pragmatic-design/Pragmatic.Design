namespace Pragmatic.Logging.Configuration;

/// <summary>
/// Main configuration options for Pragmatic.Logging.
/// Supports binding from IConfiguration and IOptionsMonitor pattern.
/// </summary>
public sealed class PragmaticLoggingOptions
{
    /// <summary>
    /// Configuration section name for binding from appsettings.json.
    /// </summary>
    public const string SectionName = "PragmaticLogging";

    /// <summary>
    /// Gets or sets whether Pragmatic.Logging is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum log level for structured messages.
    /// </summary>
    public Microsoft.Extensions.Logging.LogLevel MinimumLevel { get; set; } = Microsoft.Extensions.Logging.LogLevel.Information;

    /// <summary>
    /// Gets or sets whether to enable high-performance mode.
    /// Optimizes for throughput at the cost of some features.
    /// </summary>
    public bool HighPerformanceMode { get; set; }

    /// <summary>
    /// Gets or sets whether to enable telemetry collection.
    /// </summary>
    public bool EnableTelemetry { get; set; } = true;

    /// <summary>
    /// Gets or sets the configuration preset name.
    /// Valid values: Development, Staging, Production, HighPerformance, Security.
    /// </summary>
    public string? ConfigurationPreset { get; set; }

    /// <summary>
    /// Gets or sets privacy and data redaction options.
    /// </summary>
    public PrivacyOptions Privacy { get; set; } = new();

    /// <summary>
    /// Gets or sets audit trail configuration.
    /// </summary>
    public AuditOptions Audit { get; set; } = new();

    /// <summary>
    /// Gets or sets provider-specific configuration.
    /// </summary>
    public ProvidersOptions Providers { get; set; } = new();

    /// <summary>
    /// Gets or sets performance optimization options.
    /// </summary>
    public PerformanceOptions Performance { get; set; } = new();

    /// <summary>
    /// Gets or sets context enrichment options.
    /// </summary>
    public ContextOptions Context { get; set; } = new();

    /// <summary>
    /// Gets or sets rate limiting options.
    /// </summary>
    public RateLimitingOptions RateLimiting { get; set; } = new();
}
