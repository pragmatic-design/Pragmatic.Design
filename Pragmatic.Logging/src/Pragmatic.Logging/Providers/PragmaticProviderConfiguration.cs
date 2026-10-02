using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Configuration;
using Pragmatic.Logging.Filtering;

namespace Pragmatic.Logging.Providers;

/// <summary>
/// Default implementation of IPragmaticProviderConfiguration with validation logic.
/// </summary>
public sealed class PragmaticProviderConfiguration : IPragmaticProviderConfiguration
{
    /// <inheritdoc />
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <inheritdoc />
    public Dictionary<string, LogLevel> CategoryLevels { get; set; } = new();

    /// <inheritdoc />
    public bool IncludeStructuredProperties { get; set; } = true;

    /// <inheritdoc />
    public bool IncludeContextEnrichment { get; set; } = true;

    /// <inheritdoc />
    public ContextFilterConfiguration ContextFilter { get; set; } = new();

    /// <inheritdoc />
    public FormattingConfiguration Formatting { get; set; } = new();

    /// <inheritdoc />
    public PerformanceConfiguration Performance { get; set; } = new();

    /// <inheritdoc />
    public Dictionary<string, object?> CustomProperties { get; set; } = new();

    /// <summary>
    /// Gets or sets the filter configuration for advanced filtering.
    /// </summary>
    public FilterConfiguration Filters { get; set; } = new();

    /// <summary>
    /// Gets or sets whether high performance mode is enabled.
    /// </summary>
    public bool EnableHighPerformance { get; set; }

    /// <summary>
    /// Gets or sets the buffer size for batching operations.
    /// </summary>
    public int BufferSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets whether to enable secret detection and redaction.
    /// </summary>
    public bool EnableSecretDetection { get; set; }

    /// <summary>
    /// Gets or sets whether to enable PII (Personally Identifiable Information) redaction.
    /// </summary>
    public bool EnablePiiRedaction { get; set; }

    /// <summary>
    /// Gets or sets whether to include trace ID in log entries.
    /// </summary>
    public bool IncludeTraceId { get; set; }

    /// <summary>
    /// Gets or sets whether to include span ID in log entries.
    /// </summary>
    public bool IncludeSpanId { get; set; }

    /// <summary>
    /// Gets or sets the flush threshold for automatic flushing.
    /// </summary>
    public int FlushThreshold { get; set; } = 100;

    /// <summary>
    /// Gets or sets whether to use background processing.
    /// </summary>
    public bool UseBackgroundProcessing { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include correlation ID in logs.
    /// </summary>
    public bool IncludeCorrelationId { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include user context in logs.
    /// </summary>
    public bool IncludeUserContext { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include request context in logs.
    /// </summary>
    public bool IncludeRequestContext { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include machine context in logs.
    /// </summary>
    public bool IncludeMachineContext { get; set; } = true;

    /// <summary>
    /// Gets or sets whether telemetry is enabled.
    /// </summary>
    public bool EnableTelemetry { get; set; } = true;

    /// <summary>
    /// Gets or sets whether rate limiting is enabled.
    /// </summary>
    public bool EnableRateLimiting { get; set; }

    /// <summary>
    /// Gets or sets the rate limit strategy.
    /// </summary>
    public RateLimitStrategy RateLimitStrategy { get; set; } = RateLimitStrategy.TokenBucket;

    /// <summary>
    /// Gets or sets the maximum messages per second for rate limiting.
    /// </summary>
    public int MaxMessagesPerSecond { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the rate limit burst size.
    /// </summary>
    public int RateLimitBurstSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the filter expression for advanced expression-based filtering.
    /// When set, this takes precedence over the Filters configuration.
    /// Example: f => (f.EntityFramework(Warning) &amp;&amp; f.SlowQuery(1000)) || f.BusinessCritical()
    /// </summary>
    public FilterExpression? FilterExpression { get; set; }

    /// <inheritdoc />
    public PrivacyConfiguration Privacy { get; set; } = new();

    /// <inheritdoc />
    public IEnumerable<string> Validate()
    {
        var errors = new List<string>();

        // Validate minimum level
        if (!Enum.IsDefined(MinimumLevel))
        {
            errors.Add($"Invalid MinimumLevel: {MinimumLevel}");
        }

        // Validate category levels
        foreach (var kvp in CategoryLevels)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key))
            {
                errors.Add("Category level key cannot be null or whitespace");
            }

            if (!Enum.IsDefined(kvp.Value))
            {
                errors.Add($"Invalid log level for category '{kvp.Key}': {kvp.Value}");
            }
        }

        // Validate context filter
        if (ContextFilter.MaxDepth < 0)
        {
            errors.Add("ContextFilter.MaxDepth cannot be negative");
        }

        if (ContextFilter.MaxDepth > 10)
        {
            errors.Add("ContextFilter.MaxDepth cannot exceed 10 for performance reasons");
        }

        // Validate formatting
        if (string.IsNullOrWhiteSpace(Formatting.TimestampFormat))
        {
            errors.Add("Formatting.TimestampFormat cannot be null or whitespace");
        }

        if (string.IsNullOrWhiteSpace(Formatting.MessageTemplate))
        {
            errors.Add("Formatting.MessageTemplate cannot be null or whitespace");
        }

        if (Formatting.MaxMessageLength < 0)
        {
            errors.Add("Formatting.MaxMessageLength cannot be negative");
        }

        // Validate performance configuration
        if (Performance.BatchSize <= 0)
        {
            errors.Add("Performance.BatchSize must be positive");
        }

        if (Performance.BatchSize > 10000)
        {
            errors.Add("Performance.BatchSize cannot exceed 10000 for performance reasons");
        }

        if (Performance.FlushInterval <= TimeSpan.Zero)
        {
            errors.Add("Performance.FlushInterval must be positive");
        }

        if (Performance.MaxQueueSize <= 0)
        {
            errors.Add("Performance.MaxQueueSize must be positive");
        }

        if (!Enum.IsDefined(Performance.OverflowStrategy))
        {
            errors.Add($"Invalid Performance.OverflowStrategy: {Performance.OverflowStrategy}");
        }

        if (!Enum.IsDefined(Performance.BackgroundThreadPriority))
        {
            errors.Add($"Invalid Performance.BackgroundThreadPriority: {Performance.BackgroundThreadPriority}");
        }

        return errors;
    }

    /// <summary>
    /// Creates a deep copy of this configuration.
    /// </summary>
    /// <returns>A new configuration instance with the same values</returns>
    public PragmaticProviderConfiguration Clone()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = MinimumLevel,
            CategoryLevels = new Dictionary<string, LogLevel>(CategoryLevels),
            IncludeStructuredProperties = IncludeStructuredProperties,
            IncludeContextEnrichment = IncludeContextEnrichment,
            ContextFilter = new ContextFilterConfiguration
            {
                Mode = ContextFilter.Mode,
                PropertyNames = new HashSet<string>(ContextFilter.PropertyNames),
                PropertyPatterns = new HashSet<string>(ContextFilter.PropertyPatterns),
                IncludeRedactedProperties = ContextFilter.IncludeRedactedProperties,
                MaxDepth = ContextFilter.MaxDepth
            },
            Formatting = new FormattingConfiguration
            {
                TimestampFormat = Formatting.TimestampFormat,
                UseUtcTimestamp = Formatting.UseUtcTimestamp,
                MessageTemplate = Formatting.MessageTemplate,
                IncludeExceptionDetails = Formatting.IncludeExceptionDetails,
                MaxMessageLength = Formatting.MaxMessageLength,
                CustomFormatters = new Dictionary<Type, Func<object, string>>(Formatting.CustomFormatters),
                PrettyPrintJson = Formatting.PrettyPrintJson
            },
            Performance = new PerformanceConfiguration
            {
                EnableBatching = Performance.EnableBatching,
                BatchSize = Performance.BatchSize,
                FlushInterval = Performance.FlushInterval,
                MaxQueueSize = Performance.MaxQueueSize,
                OverflowStrategy = Performance.OverflowStrategy,
                UseZeroAllocation = Performance.UseZeroAllocation,
                BackgroundThreadPriority = Performance.BackgroundThreadPriority
            },
            CustomProperties = new Dictionary<string, object?>(CustomProperties),

            // Remaining scalar/reference properties — omitting one silently produces an incomplete
            // clone. Privacy and Filters are deep-copied via their own Clone().
            Filters = Filters.Clone(),
            EnableHighPerformance = EnableHighPerformance,
            BufferSize = BufferSize,
            EnableSecretDetection = EnableSecretDetection,
            EnablePiiRedaction = EnablePiiRedaction,
            IncludeTraceId = IncludeTraceId,
            IncludeSpanId = IncludeSpanId,
            FlushThreshold = FlushThreshold,
            UseBackgroundProcessing = UseBackgroundProcessing,
            IncludeCorrelationId = IncludeCorrelationId,
            IncludeUserContext = IncludeUserContext,
            IncludeRequestContext = IncludeRequestContext,
            IncludeMachineContext = IncludeMachineContext,
            EnableTelemetry = EnableTelemetry,
            EnableRateLimiting = EnableRateLimiting,
            RateLimitStrategy = RateLimitStrategy,
            MaxMessagesPerSecond = MaxMessagesPerSecond,
            RateLimitBurstSize = RateLimitBurstSize,
            FilterExpression = FilterExpression,
            Privacy = Privacy.Clone()
        };
    }

    /// <summary>
    /// Creates a configuration with default settings.
    /// </summary>
    /// <returns>Configuration with default settings</returns>
    public static PragmaticProviderConfiguration CreateDefault()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Information,
            IncludeStructuredProperties = true,
            IncludeContextEnrichment = true,
            Formatting = new FormattingConfiguration(),
            Performance = new PerformanceConfiguration(),
            ContextFilter = new ContextFilterConfiguration(),
            Privacy = new PrivacyConfiguration(),
            CustomProperties = new Dictionary<string, object?>()
        };
    }

    /// <summary>
    /// Creates a configuration with default settings for console output.
    /// </summary>
    /// <returns>Configuration optimized for console logging</returns>
    public static PragmaticProviderConfiguration ForConsole()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Information,
            IncludeStructuredProperties = true,
            IncludeContextEnrichment = true,
            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "HH:mm:ss.fff",
                UseUtcTimestamp = false,
                MessageTemplate = "[{Timestamp}] [{Level}] {Category}: {Message}",
                IncludeExceptionDetails = true,
                PrettyPrintJson = false
            },
            Performance = new PerformanceConfiguration
            {
                EnableBatching = false, // Console output is typically synchronous
                UseZeroAllocation = true
            }
        };
    }

    /// <summary>
    /// Creates a configuration with default settings for file output.
    /// </summary>
    /// <returns>Configuration optimized for file logging</returns>
    public static PragmaticProviderConfiguration ForFile()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Debug,
            IncludeStructuredProperties = true,
            IncludeContextEnrichment = true,
            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff",
                UseUtcTimestamp = true,
                MessageTemplate = "[{Timestamp}] [{Level}] {Category}: {Message}",
                IncludeExceptionDetails = true,
                MaxMessageLength = 32768, // Allow longer messages in files
                PrettyPrintJson = false
            },
            Performance = new PerformanceConfiguration
            {
                EnableBatching = true,
                BatchSize = 100,
                FlushInterval = TimeSpan.FromSeconds(5),
                MaxQueueSize = 10000,
                OverflowStrategy = QueueOverflowStrategy.DropOldest,
                UseZeroAllocation = true
            },
            ContextFilter = new ContextFilterConfiguration
            {
                Mode = ContextFilterMode.Include,
                PropertyNames = new HashSet<string>() // Include all context by default
            },
            CustomProperties = new Dictionary<string, object?>
            {
                ["MaxFileSize"] = 10 * 1024 * 1024L, // 10MB
                ["RollingInterval"] = "Day",
                ["MaxRetainedFiles"] = 31,
                ["FlushAfterWrite"] = true
            }
        };
    }

    /// <summary>
    /// Creates a configuration optimized for high-performance file logging.
    /// </summary>
    /// <returns>Configuration with performance optimizations for file logging</returns>
    public static PragmaticProviderConfiguration ForHighPerformanceFile()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Information,
            IncludeStructuredProperties = true,
            IncludeContextEnrichment = false, // Disable for better performance
            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff",
                UseUtcTimestamp = true,
                MessageTemplate = "[{Timestamp}] [{Level}] {Message}", // Simplified template
                IncludeExceptionDetails = true,
                MaxMessageLength = 8192, // Shorter messages for better performance
                PrettyPrintJson = false
            },
            Performance = new PerformanceConfiguration
            {
                EnableBatching = true,
                BatchSize = 500, // Larger batches
                FlushInterval = TimeSpan.FromSeconds(10), // Less frequent flushing
                MaxQueueSize = 50000, // Larger queue
                OverflowStrategy = QueueOverflowStrategy.DropOldest,
                UseZeroAllocation = true
            },
            ContextFilter = new ContextFilterConfiguration
            {
                Mode = ContextFilterMode.Exclude,
                PropertyNames = new HashSet<string> { "MachineName", "ProcessId", "ThreadId" }
            },
            CustomProperties = new Dictionary<string, object?>
            {
                ["MaxFileSize"] = 100 * 1024 * 1024L, // 100MB
                ["RollingInterval"] = "Hour",
                ["MaxRetainedFiles"] = 24,
                ["FlushAfterWrite"] = false // Better performance
            }
        };
    }


    /// <summary>
    /// Creates a configuration with default settings for structured JSON output.
    /// </summary>
    /// <returns>Configuration optimized for JSON logging</returns>
    public static PragmaticProviderConfiguration ForJson()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Debug,
            IncludeStructuredProperties = true,
            IncludeContextEnrichment = true,
            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ",
                UseUtcTimestamp = true,
                IncludeExceptionDetails = true,
                PrettyPrintJson = false // Compact JSON for performance
            },
            Performance = new PerformanceConfiguration
            {
                EnableBatching = true,
                BatchSize = 50, // Smaller batches for JSON to reduce memory
                FlushInterval = TimeSpan.FromSeconds(2),
                MaxQueueSize = 5000,
                OverflowStrategy = QueueOverflowStrategy.DropOldest,
                UseZeroAllocation = true
            },
            CustomProperties = new Dictionary<string, object?>
            {
                ["PrettyPrint"] = false,
                ["SerializeComplexObjects"] = true,
                ["SkipValidation"] = true,
                ["AutoFlush"] = false
            }
        };
    }

}

/// <summary>
/// Extension methods for provider registry to add JSON providers.
/// </summary>
public static class JsonProviderExtensions
{
    /// <param name="registry">The provider registry</param>
    extension(PragmaticLoggerProviderRegistry registry)
    {
        /// <summary>
        /// Adds a JSON provider to log structured JSON output.
        /// </summary>
        /// <param name="name">Provider name</param>
        /// <param name="configure">Optional configuration action</param>
        /// <returns>The provider registry for chaining</returns>
        public PragmaticLoggerProviderRegistry AddJsonProvider(string name = "json",
            Action<PragmaticProviderConfiguration>? configure = null)
        {
            var config = PragmaticProviderConfiguration.ForJson();
            configure?.Invoke(config);

            var provider = new PragmaticJsonProvider(name, config);
            registry.RegisterProvider(provider);

            return registry;
        }

        /// <summary>
        /// Adds a JSON file provider to log structured JSON to a file.
        /// </summary>
        /// <param name="filePath">The file path for JSON output</param>
        /// <param name="name">Provider name</param>
        /// <param name="configure">Optional configuration action</param>
        /// <returns>The provider registry for chaining</returns>
        public PragmaticLoggerProviderRegistry AddJsonFileProvider(string filePath,
            string name = "jsonfile",
            Action<PragmaticProviderConfiguration>? configure = null)
        {
            var config = PragmaticJsonConfiguration.ForHighPerformanceJsonFile();
            configure?.Invoke(config);

            var provider = new PragmaticJsonProvider(name, config, filePath);
            registry.RegisterProvider(provider);

            return registry;
        }

        /// <summary>
        /// Adds a memory provider for testing and debugging.
        /// </summary>
        /// <param name="name">Provider name</param>
        /// <param name="configure">Optional configuration action</param>
        /// <returns>The provider registry for chaining</returns>
        public PragmaticLoggerProviderRegistry AddMemoryProvider(string name = "memory",
            Action<PragmaticProviderConfiguration>? configure = null)
        {
            var config = PragmaticMemoryConfiguration.ForMemory();
            configure?.Invoke(config);

            var provider = new PragmaticMemoryProvider(name, config);
            registry.RegisterProvider(provider);

            return registry;
        }

        /// <summary>
        /// Adds a debug provider for development scenarios.
        /// </summary>
        /// <param name="name">Provider name</param>
        /// <param name="configure">Optional configuration action</param>
        /// <returns>The provider registry for chaining</returns>
        public PragmaticLoggerProviderRegistry AddDebugProvider(string name = "debug",
            Action<PragmaticProviderConfiguration>? configure = null)
        {
            var config = PragmaticDebugConfiguration.ForDebug();
            configure?.Invoke(config);

            var provider = new PragmaticDebugProvider(name, config);
            registry.RegisterProvider(provider);

            return registry;
        }

        /// <summary>
        /// Adds a null provider for benchmarking scenarios.
        /// </summary>
        /// <param name="name">Provider name</param>
        /// <param name="configure">Optional configuration action</param>
        /// <returns>The provider registry for chaining</returns>
        public PragmaticLoggerProviderRegistry AddNullProvider(string name = "null",
            Action<PragmaticProviderConfiguration>? configure = null)
        {
            var config = PragmaticNullConfiguration.ForBenchmarking();
            configure?.Invoke(config);

            var provider = new PragmaticNullProvider(name, config);
            registry.RegisterProvider(provider);

            return registry;
        }
    }
}