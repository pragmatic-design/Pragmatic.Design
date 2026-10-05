using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Filtering;

namespace Pragmatic.Logging.Providers;

/// <summary>
/// Windows Event Log provider for Pragmatic.Logging.
/// Writes log entries to the Windows Event Log using System.Diagnostics.EventLog.
/// Only available on Windows platforms - gracefully degrades on other platforms.
/// </summary>
[SupportedOSPlatform("windows")]
public class PragmaticWindowsEventLogProvider : PragmaticLoggerProviderBase
{
    private readonly EventLog? _eventLog;
    private readonly bool _isSupported;
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions _complexValueOptions = new() { WriteIndented = false };

    public PragmaticWindowsEventLogProvider(PragmaticWindowsEventLogConfiguration configuration)
        : base("WindowsEventLog", configuration)
    {
        try
        {
            // Check if running on Windows
            _isSupported = OperatingSystem.IsWindows();

            if (_isSupported)
            {
                _eventLog = InitializeEventLog(configuration);
            }
        }
        catch (Exception ex)
        {
            // Log initialization error but don't throw - graceful degradation
            System.Diagnostics.Debug.WriteLine($"Windows Event Log provider initialization failed: {ex.Message}");
            _isSupported = false;
        }
    }

    private static EventLog? InitializeEventLog(PragmaticWindowsEventLogConfiguration config)
    {
        try
        {
            var eventLog = new EventLog
            {
                Log = config.LogName,
                Source = config.SourceName
            };

            // Auto-register source if requested and not already registered
            if (config.AutoRegisterSource && !EventLog.SourceExists(config.SourceName))
            {
                try
                {
                    EventLog.CreateEventSource(new EventSourceCreationData(config.SourceName, config.LogName)
                    {
                        MachineName = config.MachineName
                    });
                }
                catch (SecurityException)
                {
                    // Source registration requires administrative privileges
                    // Continue without auto-registration - source must be manually created
                    System.Diagnostics.Debug.WriteLine($"Cannot auto-register Event Log source '{config.SourceName}' - insufficient privileges");
                }
                catch (ArgumentException ex) when (ex.Message.Contains("already registered"))
                {
                    // Source already exists for different log - this is expected
                }
            }

            return eventLog;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize Event Log: {ex.Message}");
            return null;
        }
    }

    protected override void WriteLogCore(LogEntry logEntry)
    {
        if (!_isSupported || _eventLog == null)
            return;

        try
        {
            var eventLogEntryType = MapLogLevelToEventLogEntryType(logEntry.LogLevel);
            var eventLogEventId = DetermineEventId(logEntry.EventId, logEntry.LogLevel);
            var fullMessage = FormatMessage(logEntry.Message, logEntry.Exception, logEntry.Properties);

            lock (_lock)
            {
                var config = (PragmaticWindowsEventLogConfiguration)Configuration;
                _eventLog.WriteEntry(fullMessage, eventLogEntryType, eventLogEventId,
                    config.Category);
            }
        }
        catch (Exception ex)
        {
            // Don't throw - just debug log the error
            System.Diagnostics.Debug.WriteLine($"Failed to write to Event Log: {ex.Message}");
        }
    }

    /// <remarks>
    ///     Written by hand rather than by serializing the dictionary, which reflects over each value's
    ///     runtime type and fails under Native AOT. Scalars are written as JSON scalars, a complex value
    ///     as its JSON string, from the application's JSON seam.
    /// </remarks>
    private string SerializeStructuredData(IReadOnlyDictionary<string, object?> properties, bool indented)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in properties)
            {
                writer.WritePropertyName(name);
                WriteScalarOrString(writer, value);
            }
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private void WriteScalarOrString(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string text: writer.WriteStringValue(text); break;
            case bool flag: writer.WriteBooleanValue(flag); break;
            case int number: writer.WriteNumberValue(number); break;
            case long number: writer.WriteNumberValue(number); break;
            case short number: writer.WriteNumberValue(number); break;
            case byte number: writer.WriteNumberValue(number); break;
            case uint number: writer.WriteNumberValue(number); break;
            case ulong number: writer.WriteNumberValue(number); break;
            case float number: writer.WriteNumberValue(number); break;
            case double number: writer.WriteNumberValue(number); break;
            case decimal number: writer.WriteNumberValue(number); break;
            case DateTime moment: writer.WriteStringValue(moment); break;
            case DateTimeOffset moment: writer.WriteStringValue(moment); break;
            case Guid id: writer.WriteStringValue(id); break;
            default: writer.WriteStringValue(SerializeComplexValue(value, _complexValueOptions)); break;
        }
    }

    private string FormatMessage(string message, Exception? exception, IReadOnlyDictionary<string, object?> properties)
    {
        var config = (PragmaticWindowsEventLogConfiguration)Configuration;

        if (!config.IncludeStructuredData && exception == null)
        {
            return message;
        }

        var formatted = message;

        // Add structured data if enabled
        if (config.IncludeStructuredData && properties.Any())
        {
            try
            {
                var structuredData = SerializeStructuredData(properties, config.IndentStructuredData);
                formatted += Environment.NewLine + "Structured Data: " + structuredData;
            }
            catch (Exception ex)
            {
                formatted += Environment.NewLine + $"Structured Data: <Serialization failed: {ex.Message}>";
            }
        }

        // Add exception if present
        if (exception != null)
        {
            formatted += Environment.NewLine + "Exception: " + exception.ToString();
        }

        // Truncate if too long for Event Log (max 31,839 characters)
        if (formatted.Length > config.MaxMessageLength)
        {
            formatted = string.Concat(formatted.AsSpan(0, config.MaxMessageLength - 50),
                       Environment.NewLine, "... (message truncated)");
        }

        return formatted;
    }

    private static EventLogEntryType MapLogLevelToEventLogEntryType(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Critical => EventLogEntryType.Error,
            LogLevel.Error => EventLogEntryType.Error,
            LogLevel.Warning => EventLogEntryType.Warning,
            LogLevel.Information => EventLogEntryType.Information,
            LogLevel.Debug => EventLogEntryType.Information,
            LogLevel.Trace => EventLogEntryType.Information,
            _ => EventLogEntryType.Information
        };
    }

    private int DetermineEventId(EventId eventId, LogLevel logLevel)
    {
        var config = (PragmaticWindowsEventLogConfiguration)Configuration;

        // Use provided EventId if valid
        if (eventId.Id > 0)
            return eventId.Id;

        // Use custom mapping if configured
        if (config.EventIdMapping.TryGetValue(logLevel, out var mappedId))
            return mappedId;

        // Default mapping based on log level
        return logLevel switch
        {
            LogLevel.Critical => 1,
            LogLevel.Error => 2,
            LogLevel.Warning => 3,
            LogLevel.Information => 4,
            LogLevel.Debug => 5,
            LogLevel.Trace => 6,
            _ => 0
        };
    }

    protected override void DisposeCore()
    {
        lock (_lock)
        {
            _eventLog?.Dispose();
        }
    }

    /// <summary>
    /// Gets whether the Windows Event Log provider is supported on the current platform.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsWindows();
}

/// <summary>
/// Configuration for the Windows Event Log provider.
/// </summary>
public record PragmaticWindowsEventLogConfiguration : IPragmaticProviderConfiguration
{
    /// <summary>
    /// Gets or sets the Event Log name to write to (e.g., "Application", "System", or a custom log name).
    /// Default is "Application".
    /// </summary>
    public string LogName { get; init; } = "Application";

    /// <summary>
    /// Gets or sets the Event Source name for the application.
    /// This identifies your application in the Event Log.
    /// Default is "PragmaticLogging".
    /// </summary>
    public string SourceName { get; init; } = "PragmaticLogging";

    /// <summary>
    /// Gets or sets the machine name for remote Event Log writing.
    /// Default is "." (local machine).
    /// </summary>
    public string MachineName { get; init; } = ".";

    /// <summary>
    /// Gets or sets whether to automatically register the Event Source if it doesn't exist.
    /// Note: Source registration requires administrative privileges.
    /// Default is true.
    /// </summary>
    public bool AutoRegisterSource { get; init; } = true;

    /// <summary>
    /// Gets or sets the Event Category for log entries.
    /// Default is 0 (no category).
    /// </summary>
    public short Category { get; init; }

    /// <summary>
    /// Gets or sets whether to include structured data (properties) in the Event Log message.
    /// When enabled, properties are serialized as JSON and included in the message.
    /// Default is true.
    /// </summary>
    public bool IncludeStructuredData { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to indent structured data JSON for readability.
    /// Only applies when IncludeStructuredData is true.
    /// Default is false for space efficiency.
    /// </summary>
    public bool IndentStructuredData { get; init; }

    /// <summary>
    /// Gets or sets the maximum message length before truncation.
    /// Event Log has a limit of 31,839 characters per message.
    /// Default is 30,000 to leave room for additional formatting.
    /// </summary>
    public int MaxMessageLength { get; init; } = 30000;

    /// <summary>
    /// Gets or sets custom Event ID mapping for log levels.
    /// Use this to assign specific Event IDs to different log levels.
    /// Default mappings: Critical=1, Error=2, Warning=3, Information=4, Debug=5, Trace=6.
    /// </summary>
    public Dictionary<LogLevel, int> EventIdMapping { get; init; } = new();

    /// <summary>
    /// Gets or sets whether to use the logger category name as the Event Source.
    /// When enabled, each logger category gets its own Event Source.
    /// Default is false - uses the configured SourceName for all loggers.
    /// </summary>
    public bool UseCategoryAsSource { get; init; }

    // IPragmaticProviderConfiguration interface implementation
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
    public Dictionary<string, LogLevel> CategoryLevels { get; set; } = new();
    public bool IncludeStructuredProperties { get; set; } = true;
    public bool IncludeContextEnrichment { get; set; } = true;
    public ContextFilterConfiguration ContextFilter { get; set; } = new();
    public FormattingConfiguration Formatting { get; set; } = new();
    public PerformanceConfiguration Performance { get; set; } = new();
    public PrivacyConfiguration Privacy { get; set; } = new();
    public Dictionary<string, object?> CustomProperties { get; set; } = new();
    public FilterExpression? FilterExpression { get; set; }
    public FilterConfiguration Filters { get; set; } = new();

    public IEnumerable<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(LogName))
            errors.Add("LogName cannot be null or whitespace");

        if (string.IsNullOrWhiteSpace(SourceName))
            errors.Add("SourceName cannot be null or whitespace");

        if (MaxMessageLength <= 0)
            errors.Add("MaxMessageLength must be positive");

        if (MaxMessageLength > 31839)
            errors.Add("MaxMessageLength cannot exceed 31839 (Windows Event Log limit)");

        return errors;
    }
}

/// <summary>
/// Builder extension methods for Windows Event Log provider.
/// </summary>
public static class WindowsEventLogProviderExtensions
{
    /// <param name="builder">The logging builder.</param>
    extension(PragmaticLoggingBuilder builder)
    {
        /// <summary>
        /// Adds Windows Event Log provider with default configuration.
        /// Only works on Windows - gracefully ignored on other platforms.
        /// </summary>
        /// <returns>The logging builder for method chaining.</returns>
        [SupportedOSPlatform("windows")]
        public PragmaticLoggingBuilder AddWindowsEventLog()
        {
            return builder.AddWindowsEventLog(new PragmaticWindowsEventLogConfiguration());
        }

        /// <summary>
        /// Adds Windows Event Log provider with custom configuration.
        /// Only works on Windows - gracefully ignored on other platforms.
        /// </summary>
        /// <param name="configuration">The Event Log configuration.</param>
        /// <returns>The logging builder for method chaining.</returns>
        [SupportedOSPlatform("windows")]
        public PragmaticLoggingBuilder AddWindowsEventLog(PragmaticWindowsEventLogConfiguration configuration)
        {
            if (PragmaticWindowsEventLogProvider.IsSupported)
            {
                builder.AddProvider<PragmaticWindowsEventLogProvider>(_ => new PragmaticWindowsEventLogProvider(configuration));
            }
            return builder;
        }

        /// <summary>
        /// Adds Windows Event Log provider with configuration action.
        /// Only works on Windows - gracefully ignored on other platforms.
        /// </summary>
        /// <param name="configure">Action to configure the Event Log provider.</param>
        /// <returns>The logging builder for method chaining.</returns>
        [SupportedOSPlatform("windows")]
        public PragmaticLoggingBuilder AddWindowsEventLog(Action<PragmaticWindowsEventLogConfiguration> configure)
        {
            var configuration = new PragmaticWindowsEventLogConfiguration();
            configure(configuration);
            return builder.AddWindowsEventLog(configuration);
        }

        /// <summary>
        /// Adds Windows Event Log provider with fluent configuration.
        /// Only works on Windows - gracefully ignored on other platforms.
        /// </summary>
        /// <param name="sourceName">The Event Source name for your application.</param>
        /// <param name="logName">The Event Log name (default: "Application").</param>
        /// <returns>The logging builder for method chaining.</returns>
        [SupportedOSPlatform("windows")]
        public PragmaticLoggingBuilder AddWindowsEventLog(string sourceName, string logName = "Application")
        {
            var configuration = new PragmaticWindowsEventLogConfiguration
            {
                SourceName = sourceName,
                LogName = logName
            };
            return builder.AddWindowsEventLog(configuration);
        }
    }
}

/// <summary>
/// Preset configurations for common Windows Event Log scenarios.
/// </summary>
public static class WindowsEventLogPresets
{
    /// <summary>
    /// Gets configuration for standard application logging to the Application log.
    /// </summary>
    /// <param name="applicationName">Your application name (used as Event Source).</param>
    /// <returns>Configuration for application logging.</returns>
    public static PragmaticWindowsEventLogConfiguration Application(string applicationName)
    {
        return new PragmaticWindowsEventLogConfiguration
        {
            SourceName = applicationName,
            LogName = "Application",
            IncludeStructuredData = true,
            AutoRegisterSource = true
        };
    }

    /// <summary>
    /// Gets configuration for security-focused logging to the Security log.
    /// Note: Writing to Security log requires special privileges.
    /// </summary>
    /// <param name="applicationName">Your application name (used as Event Source).</param>
    /// <returns>Configuration for security logging.</returns>
    public static PragmaticWindowsEventLogConfiguration Security(string applicationName)
    {
        return new PragmaticWindowsEventLogConfiguration
        {
            SourceName = applicationName,
            LogName = "Security",
            IncludeStructuredData = true,
            IndentStructuredData = false, // More compact for security logs
            AutoRegisterSource = false    // Security log sources must be manually registered
        };
    }

    /// <summary>
    /// Gets configuration for system service logging with custom Event IDs.
    /// </summary>
    /// <param name="serviceName">Your service name (used as Event Source).</param>
    /// <returns>Configuration for service logging.</returns>
    public static PragmaticWindowsEventLogConfiguration WindowsService(string serviceName)
    {
        return new PragmaticWindowsEventLogConfiguration
        {
            SourceName = serviceName,
            LogName = "Application",
            IncludeStructuredData = true,
            AutoRegisterSource = true,
            EventIdMapping = new Dictionary<LogLevel, int>
            {
                [LogLevel.Critical] = 1000,  // Service critical errors
                [LogLevel.Error] = 2000,     // Service errors
                [LogLevel.Warning] = 3000,   // Service warnings
                [LogLevel.Information] = 4000, // Service information
                [LogLevel.Debug] = 5000,     // Service debug info
                [LogLevel.Trace] = 6000      // Service trace info
            }
        };
    }

    /// <summary>
    /// Gets configuration optimized for high-volume logging scenarios.
    /// </summary>
    /// <param name="applicationName">Your application name (used as Event Source).</param>
    /// <returns>Configuration for high-volume logging.</returns>
    public static PragmaticWindowsEventLogConfiguration HighVolume(string applicationName)
    {
        return new PragmaticWindowsEventLogConfiguration
        {
            SourceName = applicationName,
            LogName = "Application",
            IncludeStructuredData = false,  // Reduce message size
            IndentStructuredData = false,
            MaxMessageLength = 8000,        // Smaller messages for better performance
            AutoRegisterSource = true
        };
    }

    /// <summary>
    /// Gets configuration for development/debugging with maximum detail.
    /// </summary>
    /// <param name="applicationName">Your application name (used as Event Source).</param>
    /// <returns>Configuration for development logging.</returns>
    public static PragmaticWindowsEventLogConfiguration Development(string applicationName)
    {
        return new PragmaticWindowsEventLogConfiguration
        {
            SourceName = applicationName,
            LogName = "Application",
            IncludeStructuredData = true,
            IndentStructuredData = true,    // Readable JSON for debugging
            MaxMessageLength = 30000,       // Maximum detail
            AutoRegisterSource = true,
            UseCategoryAsSource = false     // Keep single source for simplicity
        };
    }
}