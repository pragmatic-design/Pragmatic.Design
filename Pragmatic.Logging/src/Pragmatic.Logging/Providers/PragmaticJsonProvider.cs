using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Providers;

/// <summary>
/// Advanced JSON logging provider that outputs structured JSON logs with comprehensive features.
/// Optimized for high performance with minimal allocations, automatic file rolling, and flexible output targets.
/// </summary>
/// <remarks>
/// <para>
/// This provider creates structured JSON output suitable for log aggregation systems like ELK Stack,
/// Fluentd, or Azure Monitor. It supports both console and file output with automatic file rolling
/// based on size and date criteria.
/// </para>
/// <para>
/// Key features:
/// - High-performance JSON serialization using System.Text.Json
/// - Zero-allocation hot paths for common data types
/// - Automatic file rolling with configurable size and date limits
/// - Rich structured data support with type preservation
/// - Exception handling with full stack trace and inner exception support
/// - Configurable JSON formatting (pretty-print vs compact)
/// - Complex object serialization with customizable options
/// </para>
/// <para>
/// Performance characteristics:
/// - Uses UTF-8 JSON writer for optimal performance
/// - Memory pooling for JSON serialization buffers
/// - Batch writing support for file operations
/// - Minimal string allocations through span-based operations
/// </para>
/// </remarks>
/// <example>
/// Basic console JSON logging setup:
/// <code>
/// // Configure for console output
/// var jsonProvider = new PragmaticJsonProvider("json-console", 
///     PragmaticJsonConfiguration.ForJson());
/// 
/// // Add to logging builder
/// builder.Logging.ClearProviders()
///     .AddProvider(jsonProvider);
/// 
/// // Usage generates structured JSON
/// logger.LogInformation("User {UserId} performed {Action} at {Timestamp}", 
///     "john.doe", "login", DateTime.UtcNow);
/// 
/// // Output: {"@timestamp":"2024-01-15T10:30:00.000Z","@level":"INFO","@logger":"MyApp.UserService","@message":"User john.doe performed login at 2024-01-15T10:30:00.000Z","@properties":{"UserId":"john.doe","Action":"login","Timestamp":"2024-01-15T10:30:00.000Z"}}
/// </code>
/// 
/// File-based JSON logging with rolling:
/// <code>
/// // Configure for high-performance file output
/// var config = PragmaticJsonConfiguration.ForHighPerformanceJsonFile();
/// config.CustomProperties["MaxFileSizeBytes"] = 100 * 1024 * 1024; // 100MB
/// config.CustomProperties["RollByDate"] = true;
/// 
/// var fileProvider = new PragmaticJsonProvider("json-file", config, "/var/log/myapp/application.json");
/// 
/// // Automatic rolling when file size or date threshold reached
/// // Files named: application_20240115-103000.json
/// </code>
/// 
/// Pretty-printed JSON for development:
/// <code>
/// var devProvider = new PragmaticJsonProvider("json-dev", 
///     PragmaticJsonConfiguration.ForPrettyJson(), Console.Out);
/// 
/// // Output with indentation for readability:
/// // {
/// //   "@timestamp": "2024-01-15T10:30:00.000Z",
/// //   "@level": "INFO", 
/// //   "@logger": "MyApp.UserService",
/// //   "@message": "User login successful",
/// //   "@properties": {
/// //     "UserId": "john.doe",
/// //     "LoginDuration": 1250
/// //   }
/// // }
/// </code>
/// 
/// Complex object serialization:
/// <code>
/// // Custom objects are automatically serialized to JSON
/// var orderData = new { 
///     OrderId = 12345, 
///     Items = new[] { "Product A", "Product B" },
///     Customer = new { Name = "John Doe", Email = "john@example.com" }
/// };
/// 
/// logger.LogInformation("Order processed: {@Order}", orderData);
/// // Results in nested JSON structure preserving object hierarchy
/// </code>
/// </example>
public sealed partial class PragmaticJsonProvider : PragmaticLoggerProviderBase
{
    private readonly JsonWriterOptions _jsonOptions;

    // One instance for the provider's lifetime, built with the writer options: STJ keeps its type metadata
    // on the options instance, and a new one per complex value threw that cache away on every call.
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly Stream _outputStream;
    private readonly TextWriter _textWriter;
    private readonly object _writeLock = new();
    private readonly bool _ownsStream;
    private readonly string? _filePath;
    private readonly StringBuilder _stringBuilder = new(4096);

    // File-specific fields for when writing to file
    private FileStream? _fileStream;
    private StreamWriter? _streamWriter;
    private DateTime _currentFileDate = DateTime.MinValue;
    private long _currentFileSize;

    /// <summary>
    /// Initializes a new instance of the JSON provider for console output.
    /// </summary>
    /// <param name="name">Provider name</param>
    /// <param name="configuration">Provider configuration</param>
    /// <param name="outputStream">Output stream (defaults to Console.Out)</param>
    public PragmaticJsonProvider(string name, IPragmaticProviderConfiguration configuration, Stream? outputStream = null)
        : base(name, configuration)
    {
        ValidateJsonConfiguration();

        _jsonOptions = CreateJsonWriterOptions();
        _serializerOptions = CreateJsonSerializerOptions();

        if (outputStream != null)
        {
            _outputStream = outputStream;
            _textWriter = new StreamWriter(_outputStream, LogTextEncoding.Utf8, leaveOpen: true);
            _ownsStream = false;
        }
        else
        {
            // Default to console output
            _outputStream = Console.OpenStandardOutput();
            _textWriter = Console.Out;
            _ownsStream = false;
        }
    }

    /// <summary>
    /// Initializes a new instance of the JSON provider for file output.
    /// </summary>
    /// <param name="name">Provider name</param>
    /// <param name="configuration">Provider configuration</param>
    /// <param name="filePath">File path for JSON output</param>
    public PragmaticJsonProvider(string name, IPragmaticProviderConfiguration configuration, string filePath)
        : base(name, configuration)
    {
        ValidateJsonConfiguration();
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _jsonOptions = CreateJsonWriterOptions();
        _serializerOptions = CreateJsonSerializerOptions();
        _filePath = filePath;
        _ownsStream = true;

        // Initialize file stream - will be created on first write
        _outputStream = null!; // Will be set in EnsureFileCreated
        _textWriter = null!; // Will be set in EnsureFileCreated
    }

    /// <inheritdoc />
    protected override void WriteLogCore(LogEvent logEvent)
    {
        lock (_writeLock)
        {
            if (_filePath != null)
            {
                if (ShouldRollFile())
                    RollFile();
                EnsureFileCreated();
            }

            WriteJsonLogEntry(logEvent);
        }
    }

    /// <inheritdoc />
    protected override void DisposeCore()
    {
        lock (_writeLock)
        {
            if (_ownsStream)
            {
                _streamWriter?.Dispose();
                _fileStream?.Dispose();
            }
            else
            {
                _textWriter?.Flush();
            }
        }
    }

    /// <inheritdoc />
    protected override ProviderHealthStatus PerformCustomHealthCheck()
    {
        if (_filePath != null)
        {
            try
            {
                // Check if we can access the file directory
                var directory = Path.GetDirectoryName(_filePath);
                if (directory != null && !Directory.Exists(directory))
                {
                    return ProviderHealthStatus.Unhealthy;
                }

                // Check if file is writable (if it exists)
                if (File.Exists(_filePath))
                {
                    using var stream = File.OpenWrite(_filePath);
                    return ProviderHealthStatus.Healthy;
                }
            }
            catch
            {
                return ProviderHealthStatus.Degraded;
            }
        }

        return ProviderHealthStatus.Healthy;
    }

    /// <inheritdoc />
    protected override Dictionary<string, object?> GetCustomMetrics()
    {
        var metrics = new Dictionary<string, object?>
        {
            ["OutputType"] = _filePath != null ? "File" : "Stream",
            ["PrettyPrint"] = _jsonOptions.Indented,
            ["CurrentFileSize"] = _currentFileSize,
            ["SupportsUtf8"] = true
        };

        if (_filePath != null)
        {
            metrics["FilePath"] = _filePath;
            metrics["FileExists"] = File.Exists(_filePath);
            metrics["CurrentFileDate"] = _currentFileDate;
        }

        return metrics;
    }

    private void WriteJsonLogEntry(LogEvent logEvent)
    {
        _stringBuilder.Clear();

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, _jsonOptions);

        writer.WriteStartObject();

        // Core fields
        writer.WriteString("@timestamp", FormatTimestamp(logEvent.Timestamp));
        writer.WriteString("@level", GetLogLevelString(logEvent.LogLevel));
        writer.WriteString("@logger", logEvent.Category);
        writer.WriteString("@message", logEvent.Message);

        // Event ID if present
        if (logEvent.EventId.Id != 0)
        {
            writer.WriteNumber("@eventId", logEvent.EventId.Id);
            if (!string.IsNullOrEmpty(logEvent.EventId.Name))
            {
                writer.WriteString("@eventName", logEvent.EventId.Name);
            }
        }

        // Exception details if present
        if (logEvent.Exception != null)
        {
            WriteExceptionDetails(writer, logEvent.Exception);
        }

        // Message template if available
        if (!string.IsNullOrEmpty(logEvent.MessageTemplate))
        {
            writer.WriteString("@messageTemplate", logEvent.MessageTemplate);
        }

        // Structured properties
        WriteStructuredProperties(writer, logEvent.Properties);

        // Scope information
        if (logEvent.Scopes.Count > 0)
        {
            WriteScopeInformation(writer, logEvent.Scopes);
        }

        writer.WriteEndObject();
        writer.Flush();

        var jsonBytes = stream.ToArray();
        var jsonString = Encoding.UTF8.GetString(jsonBytes);

        if (_filePath != null)
        {
            _streamWriter!.WriteLine(jsonString);
            _streamWriter.Flush();
            _currentFileSize += Encoding.UTF8.GetByteCount(jsonString) + Environment.NewLine.Length;
        }
        else
        {
            _textWriter.WriteLine(jsonString);
            if (GetCustomProperty<bool>("AutoFlush", true))
            {
                _textWriter.Flush();
            }
            else
            {
                // A generated call site's line goes to the stream itself; this one must reach it first.
                _textPending = true;
            }
        }
    }

    private void WriteExceptionDetails(Utf8JsonWriter writer, Exception exception)
    {
        writer.WriteStartObject("@exception");
        writer.WriteString("type", exception.GetType().FullName);
        writer.WriteString("message", exception.Message);
        writer.WriteString("stackTrace", exception.StackTrace);

        if (exception.Data.Count > 0)
        {
            writer.WriteStartObject("data");
            foreach (var key in exception.Data.Keys)
            {
                if (key != null)
                {
                    WritePropertyValue(writer, key.ToString()!, exception.Data[key]);
                }
            }
            writer.WriteEndObject();
        }

        if (exception.InnerException != null)
        {
            writer.WritePropertyName("innerException");
            WriteExceptionDetails(writer, exception.InnerException);
        }

        writer.WriteEndObject();
    }

    private void WriteStructuredProperties(Utf8JsonWriter writer, LogEventValues properties)
    {
        if (properties.Count == 0)
            return;

        writer.WriteStartObject("@properties");
        foreach (var kvp in properties)
        {
            WritePropertyValue(writer, kvp.Key, kvp.Value);
        }
        writer.WriteEndObject();
    }

    private void WriteScopeInformation(Utf8JsonWriter writer, IReadOnlyList<KeyValuePair<string, object?>> scopes)
    {
        // An object of the scope properties; a key that more than one scope carries gets an array of its
        // values, so that no scope's value is lost and no property name repeats.
        writer.WriteStartObject("@scopes");

        var written = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < scopes.Count; i++)
        {
            var key = scopes[i].Key;
            if (!written.Add(key))
                continue;

            var repeated = false;
            for (var j = i + 1; j < scopes.Count && !repeated; j++)
                repeated = string.Equals(scopes[j].Key, key, StringComparison.Ordinal);

            if (!repeated)
            {
                WritePropertyValue(writer, key, scopes[i].Value);
                continue;
            }

            writer.WriteStartArray(key);
            for (var j = i; j < scopes.Count; j++)
            {
                if (string.Equals(scopes[j].Key, key, StringComparison.Ordinal))
                    WriteJsonValue(writer, scopes[j].Value);
            }
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private void WritePropertyValue(Utf8JsonWriter writer, string propertyName, object? value)
    {
        writer.WritePropertyName(propertyName);
        WriteJsonValue(writer, value);
    }

    private void WriteJsonValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool boolValue:
                writer.WriteBooleanValue(boolValue);
                break;
            case byte byteValue:
                writer.WriteNumberValue(byteValue);
                break;
            case sbyte sbyteValue:
                writer.WriteNumberValue(sbyteValue);
                break;
            case short shortValue:
                writer.WriteNumberValue(shortValue);
                break;
            case ushort ushortValue:
                writer.WriteNumberValue(ushortValue);
                break;
            case int intValue:
                writer.WriteNumberValue(intValue);
                break;
            case uint uintValue:
                writer.WriteNumberValue(uintValue);
                break;
            case long longValue:
                writer.WriteNumberValue(longValue);
                break;
            case ulong ulongValue:
                writer.WriteNumberValue(ulongValue);
                break;
            case float floatValue:
                writer.WriteNumberValue(floatValue);
                break;
            case double doubleValue:
                writer.WriteNumberValue(doubleValue);
                break;
            case decimal decimalValue:
                writer.WriteNumberValue(decimalValue);
                break;
            // The same formats as before ("O", "c", "D"), written from a stack buffer rather than through an
            // intermediate string.
            case DateTime dateTimeValue:
                WriteFormatted(writer, dateTimeValue, "O");
                break;
            case DateTimeOffset dateTimeOffsetValue:
                WriteFormatted(writer, dateTimeOffsetValue, "O");
                break;
            case TimeSpan timeSpanValue:
                WriteFormatted(writer, timeSpanValue, "c");
                break;
            case Guid guidValue:
                WriteFormatted(writer, guidValue, "D");
                break;
            case string stringValue:
                writer.WriteStringValue(stringValue);
                break;
            default:
                // For complex objects, serialize to string representation
                // Declared redaction already happened on the entry — see PragmaticLoggerProviderBase.
                var stringRep = GetCustomProperty<bool>("SerializeComplexObjects", true)
                    ? SerializeComplexValue(value, _serializerOptions)
                    : value.ToString() ?? "null";
                writer.WriteStringValue(stringRep);
                break;
        }
    }

    private string FormatTimestamp(DateTime timestamp)
    {
        var formatting = Configuration.Formatting;
        var effectiveTimestamp = formatting.UseUtcTimestamp ? timestamp.ToUniversalTime() : timestamp.ToLocalTime();
        return effectiveTimestamp.ToString(formatting.TimestampFormat, CultureInfo.InvariantCulture);
    }

    private static string GetLogLevelString(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRCE",
        LogLevel.Debug => "DBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "FAIL",
        LogLevel.Critical => "CRIT",
        _ => "UNKN"
    };

    private JsonWriterOptions CreateJsonWriterOptions()
    {
        var prettyPrint = GetCustomProperty<bool>("PrettyPrint", false);
        var skipValidation = GetCustomProperty<bool>("SkipValidation", true);

        return new JsonWriterOptions
        {
            Indented = prettyPrint,
            SkipValidation = skipValidation,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    private static void WriteFormatted<T>(Utf8JsonWriter writer, T value, string format) where T : ISpanFormattable
    {
        Span<char> buffer = stackalloc char[64];
        if (value.TryFormat(buffer, out var written, format, CultureInfo.InvariantCulture))
            writer.WriteStringValue(buffer[..written]);
        else
            writer.WriteStringValue(value.ToString(format, CultureInfo.InvariantCulture));
    }

    private JsonSerializerOptions CreateJsonSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = GetCustomProperty<bool>("PrettyPrint", false),
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    private T GetCustomProperty<T>(string key, T defaultValue)
    {
        if (Configuration.CustomProperties.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// Validates JSON provider configuration to ensure optimal performance.
    /// </summary>
    /// <remarks>
    /// <para>⚠️ <strong>Limit:</strong> batching is required whatever the output target, while the console
    /// provider's validation prohibits batching, so JSON output to the console cannot satisfy both. The
    /// requirement is not context-aware: it applies to console and stream output as well as to file
    /// output.</para>
    /// </remarks>
    private void ValidateJsonConfiguration()
    {
        if (!Configuration.Performance.EnableBatching)
        {
            throw new ArgumentException(
                "JSON provider should use batching — it is required for efficient structured output. " +
                "Enable batching in the performance configuration.");
        }

        // Validate required JSON-specific settings
        if (string.IsNullOrWhiteSpace(Configuration.Formatting.MessageTemplate))
        {
            throw new ArgumentException("JSON provider requires a message template for structured output");
        }
    }

    // File rolling logic (similar to file provider)
    private bool ShouldRollFile()
    {
        if (_filePath == null)
            return false;

        var maxFileSize = GetCustomProperty<long>("MaxFileSizeBytes", 100 * 1024 * 1024); // 100MB default
        var rollByDate = GetCustomProperty<bool>("RollByDate", true);

        // Check size-based rolling
        if (maxFileSize > 0 && _currentFileSize >= maxFileSize)
        {
            return true;
        }

        // Check date-based rolling 
        if (rollByDate)
        {
            var now = DateTime.Now;
            if (_currentFileDate.Date != now.Date)
            {
                return true;
            }
        }

        return false;
    }

    private void RollFile()
    {
        if (_filePath == null)
            return;

        _streamWriter?.Dispose();
        _fileStream?.Dispose();

        // Create rolled file name
        var directory = Path.GetDirectoryName(_filePath) ?? "";
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(_filePath);
        var extension = Path.GetExtension(_filePath);

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var rolledFileName = $"{fileNameWithoutExt}_{timestamp}{extension}";
        var rolledFilePath = Path.Combine(directory, rolledFileName);

        try
        {
            if (File.Exists(_filePath))
            {
                File.Move(_filePath, rolledFilePath);
            }
        }
        catch
        {
            // If rolling fails, continue with current file
        }

        _currentFileSize = 0;
        _currentFileDate = DateTime.Now;
    }

    private void EnsureFileCreated()
    {
        if (_filePath == null || (_fileStream != null && _streamWriter != null))
            return;

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _fileStream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _streamWriter = new StreamWriter(_fileStream, LogTextEncoding.Utf8);

        if (_currentFileDate == DateTime.MinValue)
        {
            _currentFileDate = DateTime.Now;
            _currentFileSize = new FileInfo(_filePath).Length;
        }
    }
}

/// <summary>
/// Configuration extensions for JSON provider.
/// </summary>
public static class PragmaticJsonConfiguration
{
    /// <summary>
    /// Creates an optimal configuration for JSON console output.
    /// </summary>
    /// <returns>JSON-optimized configuration</returns>
    public static PragmaticProviderConfiguration ForJson()
    {
        var config = new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Information,
            IncludeStructuredProperties = true,
            IncludeContextEnrichment = true,
            ContextFilter =
            {
                Mode = ContextFilterMode.Exclude,
                PropertyNames = { "MachineName", "ProcessId" } // Exclude noisy properties
            },
            Formatting =
            {
                TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ",
                UseUtcTimestamp = true,
                IncludeExceptionDetails = true,
                MaxMessageLength = 0, // No limit for JSON
                PrettyPrintJson = false
            },
            Performance =
            {
                EnableBatching = true, // JSON benefits from batching
                BatchSize = 50,
                FlushInterval = TimeSpan.FromSeconds(2),
                UseZeroAllocation = true,
                MaxQueueSize = 5000,
                OverflowStrategy = QueueOverflowStrategy.DropOldest
            }
        };

        // JSON-specific custom properties
        config.CustomProperties["PrettyPrint"] = false;
        config.CustomProperties["SerializeComplexObjects"] = true;
        config.CustomProperties["SkipValidation"] = true;
        config.CustomProperties["AutoFlush"] = false; // Let batching handle flushing

        return config;
    }

    /// <summary>
    /// Creates an optimal configuration for high-performance JSON file output.
    /// </summary>
    /// <returns>High-performance JSON file configuration</returns>
    public static PragmaticProviderConfiguration ForHighPerformanceJsonFile()
    {
        var config = ForJson();

        // Override for file-specific optimizations
        config.Performance.EnableBatching = true;
        config.Performance.BatchSize = 100;
        config.Performance.FlushInterval = TimeSpan.FromSeconds(5);
        config.Performance.MaxQueueSize = 10000;

        // File rolling configuration
        config.CustomProperties["MaxFileSizeBytes"] = 500L * 1024 * 1024; // 500MB
        config.CustomProperties["RollByDate"] = true;
        config.CustomProperties["AutoFlush"] = false;

        return config;
    }

    /// <summary>
    /// Creates a configuration for human-readable JSON output (development/debugging).
    /// </summary>
    /// <returns>Pretty-print JSON configuration</returns>
    public static PragmaticProviderConfiguration ForPrettyJson()
    {
        var config = ForJson();

        config.MinimumLevel = LogLevel.Debug; // More verbose for development
        config.Formatting.PrettyPrintJson = true;

        // Pretty print settings
        config.CustomProperties["PrettyPrint"] = true;
        config.CustomProperties["SerializeComplexObjects"] = true;
        config.CustomProperties["AutoFlush"] = true; // Immediate feedback for development

        // Smaller batches for immediate feedback
        config.Performance.BatchSize = 10;
        config.Performance.FlushInterval = TimeSpan.FromSeconds(1);

        return config;
    }
}