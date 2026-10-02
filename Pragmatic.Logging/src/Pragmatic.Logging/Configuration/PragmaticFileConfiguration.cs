using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Configuration;

/// <summary>
/// Provides pre-configured file logging configurations for common scenarios.
/// These factory methods create optimized configurations for different use cases,
/// eliminating the need for complex manual configuration.
/// </summary>
/// <remarks>
/// <para>
/// File logging configurations need to balance several concerns:
/// </para>
/// <list type="bullet">
/// <item><description><strong>Performance</strong> - Batching, buffer sizes, flush intervals</description></item>
/// <item><description><strong>Reliability</strong> - Data persistence, error handling, recovery</description></item>
/// <item><description><strong>Maintenance</strong> - File rolling, retention, compression</description></item>
/// <item><description><strong>Observability</strong> - Structured data, correlation, metrics</description></item>
/// </list>
/// </remarks>
/// <example>
/// Production file logging setup:
/// <code>
/// var fileProvider = new PragmaticFileProvider(
///     "production-file",
///     PragmaticFileConfiguration.ForProduction(),
///     "/var/log/myapp/application-{Date}.log");
/// 
/// builder.Logging.AddProvider(fileProvider);
/// </code>
/// 
/// High-throughput logging configuration:
/// <code>
/// var highThroughputProvider = new PragmaticFileProvider(
///     "high-throughput-file", 
///     PragmaticFileConfiguration.ForHighThroughput(),
///     "/var/log/myapp/high-volume-{DateTime}.log");
/// </code>
/// 
/// Development logging with detailed output:
/// <code>
/// var devProvider = new PragmaticFileProvider(
///     "development-file",
///     PragmaticFileConfiguration.ForDevelopment(), 
///     "./logs/debug-{Date}.log");
/// </code>
/// </example>
public static class PragmaticFileConfiguration
{
    /// <summary>
    /// Creates a production-ready file logging configuration with balanced performance and reliability.
    /// </summary>
    /// <returns>A configuration optimized for production environments</returns>
    /// <remarks>
    /// <para>This configuration provides:</para>
    /// <list type="bullet">
    /// <item><description>Moderate batching for good performance without excessive memory usage</description></item>
    /// <item><description>Daily file rolling with 30-day retention</description></item>
    /// <item><description>Error-level minimum logging to reduce noise</description></item>
    /// <item><description>Context enrichment for troubleshooting</description></item>
    /// <item><description>Structured properties for log analysis</description></item>
    /// </list>
    /// </remarks>
    public static PragmaticProviderConfiguration ForProduction()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Information,
            IncludeStructuredProperties = true,
            IncludeContextEnrichment = true,

            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff",
                UseUtcTimestamp = true,
                MessageTemplate = "[{Timestamp}] [{Level}] {Category}: {Message}",
                IncludeExceptionDetails = true,
                MaxMessageLength = 4096,
                PrettyPrintJson = false
            },

            Performance = new PerformanceConfiguration
            {
                EnableBatching = true,
                BatchSize = 50,
                FlushInterval = TimeSpan.FromSeconds(10),
                UseZeroAllocation = true,
                MaxQueueSize = 5000
            },

            ContextFilter = new ContextFilterConfiguration
            {
                Mode = ContextFilterMode.Include,
                PropertyNames = new HashSet<string> { "RequestId", "CorrelationId", "UserId", "TraceId" }
            },

            CustomProperties = new Dictionary<string, object?>
            {
                ["MaxFileSize"] = 100L * 1024 * 1024, // 100MB
                ["RollingInterval"] = "Day",
                ["MaxRetainedFiles"] = 30,
                ["CompressOldFiles"] = true,
                ["FlushAfterWrite"] = false,
                ["BackPressurePolicy"] = "DropOldest",
                ["EnableHealthCheck"] = true,
                ["FileBufferSize"] = 8192
            }
        };
    }

    /// <summary>
    /// Creates a high-throughput file logging configuration optimized for maximum performance.
    /// </summary>
    /// <returns>A configuration optimized for high-volume logging scenarios</returns>
    /// <remarks>
    /// <para>This configuration provides:</para>
    /// <list type="bullet">
    /// <item><description>Large batches and queues for maximum throughput</description></item>
    /// <item><description>Aggressive caching and zero-allocation patterns</description></item>
    /// <item><description>Hourly file rolling to manage file sizes</description></item>
    /// <item><description>Minimal formatting overhead</description></item>
    /// <item><description>Optimized for write-heavy workloads</description></item>
    /// </list>
    /// <para><strong>Trade-offs:</strong> Higher memory usage, potential for larger data loss on crash</para>
    /// </remarks>
    public static PragmaticProviderConfiguration ForHighThroughput()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Information,
            IncludeStructuredProperties = false, // Reduce processing overhead
            IncludeContextEnrichment = false,

            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "HH:mm:ss.fff", // Shorter timestamp for performance
                UseUtcTimestamp = true,
                MessageTemplate = "[{Timestamp}] [{Level}] {Message}", // Simplified template
                IncludeExceptionDetails = true,
                MaxMessageLength = 2048, // Smaller max length
                PrettyPrintJson = false
            },

            Performance = new PerformanceConfiguration
            {
                EnableBatching = true,
                BatchSize = 500, // Large batches
                FlushInterval = TimeSpan.FromSeconds(30), // Longer flush interval
                UseZeroAllocation = true,
                MaxQueueSize = 50000 // Large queue
            },

            ContextFilter = new ContextFilterConfiguration
            {
                Mode = ContextFilterMode.Exclude,
                PropertyNames = new HashSet<string>() // Exclude all context for performance
            },

            CustomProperties = new Dictionary<string, object?>
            {
                ["MaxFileSize"] = 500L * 1024 * 1024, // 500MB - larger files
                ["RollingInterval"] = "Hour", // More frequent rolling
                ["MaxRetainedFiles"] = 168, // 1 week of hourly files
                ["CompressOldFiles"] = true,
                ["FlushAfterWrite"] = false,
                ["BackPressurePolicy"] = "DropOldest",
                ["EnableHealthCheck"] = true,
                ["FileBufferSize"] = 32768, // Larger buffer
                ["PreallocateFiles"] = true,
                ["UseDedicatedThread"] = true
            }
        };
    }

    /// <summary>
    /// Creates a development-friendly file logging configuration with detailed output and debugging features.
    /// </summary>
    /// <returns>A configuration optimized for development and debugging</returns>
    /// <remarks>
    /// <para>This configuration provides:</para>
    /// <list type="bullet">
    /// <item><description>Verbose logging including Debug and Trace levels</description></item>
    /// <item><description>Rich structured data for analysis</description></item>
    /// <item><description>Immediate flushing for real-time debugging</description></item>
    /// <item><description>Full context enrichment for troubleshooting</description></item>
    /// <item><description>Pretty-printed JSON for readability</description></item>
    /// </list>
    /// <para><strong>Trade-offs:</strong> Lower performance, larger log files, more verbose output</para>
    /// </remarks>
    public static PragmaticProviderConfiguration ForDevelopment()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Trace, // Include all levels
            IncludeStructuredProperties = true,
            IncludeContextEnrichment = true,

            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff",
                UseUtcTimestamp = false, // Local time for development
                MessageTemplate = "[{Timestamp}] [{Level}] {Category}: {Message}",
                IncludeExceptionDetails = true,
                MaxMessageLength = 0, // No limit for development
                PrettyPrintJson = true // Pretty-print for readability
            },

            Performance = new PerformanceConfiguration
            {
                EnableBatching = false, // Immediate writes for debugging
                BatchSize = 1,
                FlushInterval = TimeSpan.FromSeconds(1),
                UseZeroAllocation = false, // Prioritize debugging over performance
                MaxQueueSize = 1000
            },

            ContextFilter = new ContextFilterConfiguration
            {
                Mode = ContextFilterMode.Include,
                PropertyNames = new HashSet<string>
                {
                    "RequestId", "CorrelationId", "UserId", "TraceId",
                    "MachineName", "ProcessId", "ThreadId", "Assembly"
                }
            },

            CustomProperties = new Dictionary<string, object?>
            {
                ["MaxFileSize"] = 10L * 1024 * 1024, // 10MB - smaller files for development
                ["RollingInterval"] = "Day",
                ["MaxRetainedFiles"] = 7, // Keep only a week
                ["CompressOldFiles"] = false, // No compression for easier access
                ["FlushAfterWrite"] = true, // Immediate flush
                ["BackPressurePolicy"] = "Block", // Don't drop in development
                ["EnableHealthCheck"] = true,
                ["FileBufferSize"] = 1024, // Small buffer for immediate writes
                ["IncludeDebugInfo"] = true
            }
        };
    }

    /// <summary>
    /// Creates a secure file logging configuration with enhanced security features and compliance support.
    /// </summary>
    /// <returns>A configuration optimized for secure and compliant logging</returns>
    /// <remarks>
    /// <para>This configuration provides:</para>
    /// <list type="bullet">
    /// <item><description>Enhanced security with integrity checking</description></item>
    /// <item><description>Audit trail compliance features</description></item>
    /// <item><description>PII redaction and data protection</description></item>
    /// <item><description>Tamper-evident logging with checksums</description></item>
    /// <item><description>Extended retention for compliance</description></item>
    /// </list>
    /// </remarks>
    public static PragmaticProviderConfiguration ForSecure()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Information,
            IncludeStructuredProperties = true,
            IncludeContextEnrichment = true,

            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ", // ISO 8601 UTC
                UseUtcTimestamp = true,
                MessageTemplate = "[{Timestamp}] [{Level}] {Category}: {Message}",
                IncludeExceptionDetails = true,
                MaxMessageLength = 2048,
                PrettyPrintJson = false
            },

            Performance = new PerformanceConfiguration
            {
                EnableBatching = true,
                BatchSize = 25, // Smaller batches for security
                FlushInterval = TimeSpan.FromSeconds(5),
                UseZeroAllocation = true,
                MaxQueueSize = 2000
            },

            ContextFilter = new ContextFilterConfiguration
            {
                Mode = ContextFilterMode.Include,
                PropertyNames = new HashSet<string> { "RequestId", "CorrelationId", "AuditId", "SecurityContext" }
            },

            CustomProperties = new Dictionary<string, object?>
            {
                ["MaxFileSize"] = 50L * 1024 * 1024, // 50MB
                ["RollingInterval"] = "Day",
                ["MaxRetainedFiles"] = 2555, // 7 years for compliance
                ["CompressOldFiles"] = true,
                ["FlushAfterWrite"] = true, // Immediate flush for security
                ["BackPressurePolicy"] = "Block", // Don't drop security logs
                ["EnableHealthCheck"] = true,
                ["FileBufferSize"] = 4096,
                ["EnableIntegrityCheck"] = true,
                ["CalculateChecksum"] = true,
                ["RequireSecureDirectory"] = true,
                ["EnablePiiRedaction"] = true,
                ["AuditTrailMode"] = true
            }
        };
    }

    /// <summary>
    /// Creates a minimal file logging configuration for testing and temporary scenarios.
    /// </summary>
    /// <returns>A lightweight configuration with minimal overhead</returns>
    /// <remarks>
    /// <para>This configuration provides:</para>
    /// <list type="bullet">
    /// <item><description>Minimal resource usage</description></item>
    /// <item><description>Simple text format without structured data</description></item>
    /// <item><description>No batching or complex processing</description></item>
    /// <item><description>Basic file management</description></item>
    /// </list>
    /// </remarks>
    public static PragmaticProviderConfiguration ForMinimal()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Warning, // Only warnings and above
            IncludeStructuredProperties = false,
            IncludeContextEnrichment = false,

            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "HH:mm:ss",
                UseUtcTimestamp = true,
                MessageTemplate = "[{Timestamp}] {Level}: {Message}",
                IncludeExceptionDetails = false,
                MaxMessageLength = 1024,
                PrettyPrintJson = false
            },

            Performance = new PerformanceConfiguration
            {
                EnableBatching = false,
                BatchSize = 1,
                FlushInterval = TimeSpan.FromSeconds(1),
                UseZeroAllocation = false,
                MaxQueueSize = 100
            },

            ContextFilter = new ContextFilterConfiguration
            {
                Mode = ContextFilterMode.Exclude,
                PropertyNames = new HashSet<string>()
            },

            CustomProperties = new Dictionary<string, object?>
            {
                ["MaxFileSize"] = 5L * 1024 * 1024, // 5MB
                ["RollingInterval"] = "Day",
                ["MaxRetainedFiles"] = 3,
                ["CompressOldFiles"] = false,
                ["FlushAfterWrite"] = true,
                ["BackPressurePolicy"] = "DropNewest",
                ["EnableHealthCheck"] = false,
                ["FileBufferSize"] = 512
            }
        };
    }
}