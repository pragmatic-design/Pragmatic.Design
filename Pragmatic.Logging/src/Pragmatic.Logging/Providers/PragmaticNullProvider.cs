using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Providers;

/// <summary>
/// Null logging provider that discards all log messages without performing any output operations.
/// Designed for benchmarking and performance testing scenarios where logging overhead needs to be measured.
/// </summary>
public sealed class PragmaticNullProvider : PragmaticLoggerProviderBase
{
    private long _messagesDiscarded;

    /// <summary>
    /// Initializes a new instance of the Null provider.
    /// </summary>
    /// <param name="name">Provider name</param>
    /// <param name="configuration">Provider configuration</param>
    public PragmaticNullProvider(string name, IPragmaticProviderConfiguration configuration)
        : base(name, configuration)
    {
        // Null provider has no configuration validation requirements
        // It should work in any scenario and with any configuration
    }

    /// <summary>
    /// Gets the total number of messages that have been discarded by this provider.
    /// </summary>
    public long MessagesDiscarded => _messagesDiscarded;

    /// <inheritdoc />
    /// <remarks>A null sink consumes the typed state directly: nothing to render, nothing to store.</remarks>
    protected internal override bool SupportsDeferredWrite => true;

    /// <inheritdoc />
    protected override void WriteLogCoreDeferred<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter,
        string category)
    {
        Interlocked.Increment(ref _messagesDiscarded);
    }

    /// <inheritdoc />
    protected override void WriteLogCore(LogEvent logEvent)
    {
        // Null provider discards all messages without any processing
        // This provides the absolute minimum overhead for benchmarking
        Interlocked.Increment(ref _messagesDiscarded);
    }

    /// <inheritdoc />
    protected override ProviderHealthStatus PerformCustomHealthCheck()
    {
        // Null provider is always healthy since it never fails
        return ProviderHealthStatus.Healthy;
    }

    /// <inheritdoc />
    protected override Dictionary<string, object?> GetCustomMetrics()
    {
        return new Dictionary<string, object?>
        {
            ["MessagesDiscarded"] = _messagesDiscarded,
            ["OutputType"] = "Null",
            ["BenchmarkMode"] = true,
            ["HasSideEffects"] = false,
            ["ProcessingOverhead"] = "Minimal",
            ["ThreadSafe"] = true,
            ["PerformanceImpact"] = "None"
        };
    }

    /// <inheritdoc />
    protected override void OnConfigurationUpdated(IPragmaticProviderConfiguration oldConfig, IPragmaticProviderConfiguration newConfig)
    {
        // Null provider doesn't need to react to configuration changes
        // since it doesn't perform any actual output operations
    }

    /// <summary>
    /// Resets the discarded message counter to zero.
    /// Useful for benchmarking scenarios where you want to measure from a clean state.
    /// </summary>
    public void ResetCounters()
    {
        Interlocked.Exchange(ref _messagesDiscarded, 0);
    }
}

/// <summary>
/// Configuration extensions for Null provider.
/// </summary>
public static class PragmaticNullConfiguration
{
    /// <summary>
    /// Creates an optimal configuration for null provider benchmarking.
    /// </summary>
    /// <returns>Benchmark-optimized configuration</returns>
    public static PragmaticProviderConfiguration ForBenchmarking()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Trace, // Accept all messages for comprehensive benchmarking
            IncludeStructuredProperties = false, // Minimize processing overhead
            IncludeContextEnrichment = false, // No context processing needed
            ContextFilter =
            {
                Mode = ContextFilterMode.Include, // Minimal context processing
                PropertyNames = new HashSet<string>() // Empty set for no context
            },
            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "s", // Minimal format placeholder
                UseUtcTimestamp = false,
                MessageTemplate = "{Message}", // Minimal template placeholder  
                IncludeExceptionDetails = false, // Skip exception formatting
                MaxMessageLength = 0 // No length restrictions
            },
            Performance = new PerformanceConfiguration
            {
                EnableBatching = false, // Direct processing for minimal overhead
                UseZeroAllocation = true, // Optimize for performance
                MaxQueueSize = 1 // Minimal queue size
            },
            CustomProperties = new Dictionary<string, object?>
            {
                ["BenchmarkMode"] = true,
                ["DiscardMessages"] = true,
                ["MinimalProcessing"] = true
            }
        };
    }

    /// <summary>
    /// Creates a configuration for performance testing with structured properties enabled.
    /// </summary>
    /// <returns>Structured benchmark configuration</returns>
    public static PragmaticProviderConfiguration ForStructuredBenchmarking()
    {
        var config = ForBenchmarking();
        config.IncludeStructuredProperties = true; // Enable to measure structured logging overhead
        config.CustomProperties["TestStructuredProperties"] = true;
        return config;
    }

    /// <summary>
    /// Creates a configuration for testing context enrichment overhead.
    /// </summary>
    /// <returns>Context benchmark configuration</returns>
    public static PragmaticProviderConfiguration ForContextBenchmarking()
    {
        var config = ForBenchmarking();
        config.IncludeContextEnrichment = true; // Enable to measure context overhead
        config.ContextFilter.Mode = ContextFilterMode.All; // Include all context
        config.CustomProperties["TestContextEnrichment"] = true;
        return config;
    }

    /// <summary>
    /// Creates a configuration for testing batching overhead.
    /// </summary>
    /// <returns>Batching benchmark configuration</returns>
    public static PragmaticProviderConfiguration ForBatchingBenchmarking()
    {
        var config = ForBenchmarking();
        config.Performance.EnableBatching = true; // Enable to measure batching overhead
        config.Performance.BatchSize = 100;
        config.Performance.FlushInterval = TimeSpan.FromMilliseconds(10);
        config.Performance.MaxQueueSize = 1000;
        config.CustomProperties["TestBatching"] = true;
        return config;
    }

    /// <summary>
    /// Creates a configuration that mimics a typical production setup for comparative benchmarking.
    /// </summary>
    /// <returns>Production-like benchmark configuration</returns>
    public static PragmaticProviderConfiguration ForProductionBenchmarking()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Information, // Typical production level
            IncludeStructuredProperties = true,
            IncludeContextEnrichment = true,
            ContextFilter =
            {
                Mode = ContextFilterMode.Include,
                PropertyNames = new HashSet<string> { "CorrelationId", "UserId", "RequestId" }
            },
            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ",
                UseUtcTimestamp = true,
                MessageTemplate = "[{Timestamp}] [{Level}] {Category}: {Message}",
                IncludeExceptionDetails = true,
                MaxMessageLength = 32768
            },
            Performance = new PerformanceConfiguration
            {
                EnableBatching = true,
                BatchSize = 50,
                FlushInterval = TimeSpan.FromSeconds(1),
                MaxQueueSize = 5000,
                OverflowStrategy = QueueOverflowStrategy.DropOldest,
                UseZeroAllocation = true
            },
            CustomProperties = new Dictionary<string, object?>
            {
                ["BenchmarkMode"] = true,
                ["ProductionLike"] = true,
                ["FullProcessing"] = true
            }
        };
    }
}