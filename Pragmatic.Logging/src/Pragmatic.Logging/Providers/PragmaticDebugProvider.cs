using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Providers;

/// <summary>
/// Debug logging provider that outputs to System.Diagnostics.Debug for development scenarios.
/// Provides enhanced formatting and conditional compilation support for debug builds.
/// </summary>
public sealed class PragmaticDebugProvider : PragmaticLoggerProviderBase
{
    private readonly object _writeLock = new();
    private readonly bool _includeTimestamp;
    private readonly bool _includeThreadInfo;
    private readonly bool _includeCategory;
    private readonly string _categoryFilter;

    /// <summary>
    /// Initializes a new instance of the Debug provider.
    /// </summary>
    /// <param name="name">Provider name</param>
    /// <param name="configuration">Provider configuration</param>
    public PragmaticDebugProvider(string name, IPragmaticProviderConfiguration configuration)
        : base(name, configuration)
    {
        ValidateDebugConfiguration();
        _includeTimestamp = GetCustomProperty<bool>("IncludeTimestamp", true);
        _includeThreadInfo = GetCustomProperty<bool>("IncludeThreadInfo", true);
        _includeCategory = GetCustomProperty<bool>("IncludeCategory", true);
        _categoryFilter = GetCustomProperty<string>("CategoryFilter", string.Empty);
    }

    /// <inheritdoc />
    protected override void WriteLogCore(LogEvent logEvent)
    {
        // Skip if category doesn't match filter
        if (!string.IsNullOrEmpty(_categoryFilter) &&
            !logEvent.Category.Contains(_categoryFilter, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_writeLock)
        {
            var formattedMessage = FormatDebugMessage(logEvent);

            // Use System.Diagnostics.Trace so output reaches attached TraceListeners
            // in both Debug and Release builds. Debug.WriteLine is conditional on the
            // DEBUG symbol and is silently dropped in Release, which defeats the
            // purpose of a logging provider.
            Trace.WriteLine(formattedMessage);
        }
    }

    /// <inheritdoc />
    protected override ProviderHealthStatus PerformCustomHealthCheck()
    {
        // Debug provider is always healthy when debugger is attached
        if (Debugger.IsAttached)
            return ProviderHealthStatus.Healthy;

        // Check if running in debug mode
#if DEBUG
        return ProviderHealthStatus.Healthy;
#else
        return ProviderHealthStatus.Warning; // Warn in release builds
#endif
    }

    /// <inheritdoc />
    protected override Dictionary<string, object?> GetCustomMetrics()
    {
        return new Dictionary<string, object?>
        {
            ["DebuggerAttached"] = Debugger.IsAttached,
            ["IsDebugBuild"] = IsDebugBuild(),
            ["DebuggerAttachedMetrics"] = Debugger.IsAttached,
            ["IncludeTimestamp"] = _includeTimestamp,
            ["IncludeThreadInfo"] = _includeThreadInfo,
            ["IncludeCategory"] = _includeCategory,
            ["CategoryFilter"] = _categoryFilter,
            ["OutputType"] = "Debug",
            ["ConditionalCompilation"] = GetConditionalCompilationStatus()
        };
    }

    /// <inheritdoc />
    protected override void OnConfigurationUpdated(IPragmaticProviderConfiguration oldConfig, IPragmaticProviderConfiguration newConfig)
    {
        // Debug provider configuration is typically static, but we can handle updates
        // Most properties are read during construction and don't need runtime updates
    }

    private static void ValidateDebugConfiguration()
    {
        // Debug provider has minimal validation requirements
        // It should work in any scenario, even without a debugger
    }

    private string FormatDebugMessage(LogEvent logEvent)
    {
        var sb = new StringBuilder();

        // Add timestamp if enabled
        if (_includeTimestamp)
        {
            sb.Append('[');
            sb.Append(logEvent.FormatTimestamp(Configuration.Formatting.TimestampFormat, Configuration.Formatting.UseUtcTimestamp));
            sb.Append("] ");
        }

        // Add thread info if enabled
        if (_includeThreadInfo)
        {
            sb.Append(CultureInfo.InvariantCulture, $"[T{Environment.CurrentManagedThreadId:D2}] ");
        }

        // Add log level with color-coded indicators for debug visibility
        sb.Append('[');
        sb.Append(GetDebugLevelIndicator(logEvent.LogLevel));
        sb.Append("] ");

        // Add category if enabled
        if (_includeCategory)
        {
            sb.Append(logEvent.Category);
            sb.Append(": ");
        }

        // Add the main message
        sb.Append(logEvent.Message);

        // Add structured properties if available and enabled
        if (Configuration.IncludeStructuredProperties && logEvent.Properties.Count > 0)
        {
            sb.Append(" {");
            var first = true;
            foreach (var prop in logEvent.Properties)
            {
                if (!first)
                    sb.Append(", ");
                sb.Append(prop.Key);
                sb.Append('=');
                sb.Append(FormatPropertyValue(prop.Value));
                first = false;
            }
            sb.Append('}');
        }

        // Add exception details if present
        if (logEvent.Exception != null && Configuration.Formatting.IncludeExceptionDetails)
        {
            sb.AppendLine();
            sb.Append("Exception: ");
            sb.Append(logEvent.Exception.ToString());
        }

        return sb.ToString();
    }

    private static string GetDebugLevelIndicator(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "TRCE",
            LogLevel.Debug => "DBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "FAIL",
            LogLevel.Critical => "CRIT",
            _ => "UNKN"
        };
    }

    private static string FormatPropertyValue(object? value)
    {
        return value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString(),
            _ => value.ToString() ?? "null"
        };
    }

    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private static string GetConditionalCompilationStatus()
    {
        var symbols = new List<string>();

#if DEBUG
        symbols.Add("DEBUG");
#endif

#if TRACE
        symbols.Add("TRACE");
#endif

        return string.Join(", ", symbols);
    }

    private T GetCustomProperty<T>(string key, T defaultValue)
    {
        if (Configuration.CustomProperties.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return defaultValue;
    }
}

/// <summary>
/// Configuration extensions for Debug provider.
/// </summary>
public static class PragmaticDebugConfiguration
{
    /// <summary>
    /// Creates an optimal configuration for debug output during development.
    /// </summary>
    /// <returns>Debug-optimized configuration</returns>
    public static PragmaticProviderConfiguration ForDebug()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Trace, // Capture everything during debugging
            IncludeStructuredProperties = true,
            IncludeContextEnrichment = false, // Reduce noise in debug output
            ContextFilter =
            {
                Mode = ContextFilterMode.Include,
                PropertyNames = new HashSet<string> { "CorrelationId", "UserId" } // Only essential context
            },
            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "HH:mm:ss.fff",
                UseUtcTimestamp = false, // Local time for debugging
                MessageTemplate = "[{Timestamp}] [{Level}] {Category}: {Message}",
                IncludeExceptionDetails = true,
                MaxMessageLength = 0 // No limit for debug output
            },
            Performance = new PerformanceConfiguration
            {
                EnableBatching = false, // Immediate output for debugging
                UseZeroAllocation = false, // Prioritize functionality over performance
                MaxQueueSize = 1000 // Small queue for debug scenarios
            },
            CustomProperties = new Dictionary<string, object?>
            {
                ["IncludeTimestamp"] = true,
                ["IncludeThreadInfo"] = true,
                ["IncludeCategory"] = true,
                ["CategoryFilter"] = string.Empty // No category filtering by default
            }
        };
    }

    /// <summary>
    /// Creates a configuration for focused debugging with category filtering.
    /// </summary>
    /// <param name="categoryFilter">Category filter to focus on specific components</param>
    /// <returns>Focused debug configuration</returns>
    public static PragmaticProviderConfiguration ForFocusedDebug(string categoryFilter)
    {
        var config = ForDebug();
        config.CustomProperties["CategoryFilter"] = categoryFilter;
        config.MinimumLevel = LogLevel.Debug; // Less verbose for focused debugging
        return config;
    }

    /// <summary>
    /// Creates a configuration for lightweight debug output with minimal overhead.
    /// </summary>
    /// <returns>Lightweight debug configuration</returns>
    public static PragmaticProviderConfiguration ForLightweightDebug()
    {
        var config = ForDebug();
        config.MinimumLevel = LogLevel.Information; // Less verbose
        config.IncludeStructuredProperties = false; // Reduce output
        config.CustomProperties["IncludeThreadInfo"] = false;
        config.CustomProperties["IncludeTimestamp"] = false;
        config.CustomProperties["IncludeCategory"] = false;
        return config;
    }

    /// <summary>
    /// Creates a configuration for performance debugging with timing information.
    /// </summary>
    /// <returns>Performance debug configuration</returns>
    public static PragmaticProviderConfiguration ForPerformanceDebug()
    {
        var config = ForDebug();
        config.Formatting.TimestampFormat = "HH:mm:ss.ffffff"; // Microsecond precision
        config.CustomProperties["IncludeThreadInfo"] = true;
        config.ContextFilter.PropertyNames.Add("Duration");
        config.ContextFilter.PropertyNames.Add("ExecutionTime");
        return config;
    }
}