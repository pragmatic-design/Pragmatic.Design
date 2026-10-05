using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Providers;

/// <summary>
/// Enhanced JSON logging provider with NDJSON support, async buffering, and atomic writes.
/// Optimized for high-throughput scenarios with zero-allocation patterns and advanced file management.
/// </summary>
public sealed class PragmaticEnhancedJsonProvider : PragmaticLoggerProviderBase
{
    private readonly JsonWriterOptions _jsonOptions;
    private readonly JsonSerializerOptions _serializerOptions;

    // Reusable per-thread buffer + writer to avoid allocating a MemoryStream/Utf8JsonWriter and
    // calling stream.ToArray() on every NDJSON serialization. ArrayBufferWriter is reset (not
    // reallocated) between uses; the Utf8JsonWriter is re-targeted at the same buffer.
    private readonly ThreadLocal<(ArrayBufferWriter<byte> Buffer, Utf8JsonWriter Writer)> _ndjsonWriter;
    private readonly string? _filePath;
    private readonly bool _isNdjsonFormat;
    private readonly bool _enableAsyncBuffering;
    private readonly bool _atomicWrites;

    // Async buffering infrastructure
    private readonly ConcurrentQueue<LogEntry> _logQueue = new();
    private readonly Timer? _flushTimer;
    private readonly SemaphoreSlim _flushSemaphore = new(1, 1);
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    // File management
    private FileStream? _fileStream;
    private StreamWriter? _streamWriter;
    private readonly object _fileLock = new();
    private DateTime _currentFileDate = DateTime.MinValue;
    private long _currentFileSize;
    private int _pendingLogCount;

    // Performance tracking
    private long _totalBytesWritten;
    private long _totalLogsProcessed;
    private DateTime _lastFlushTime = DateTime.UtcNow;

    /// <summary>
    /// Initializes a new enhanced JSON provider.
    /// </summary>
    /// <param name="name">Provider name</param>
    /// <param name="configuration">Provider configuration</param>
    /// <param name="filePath">File path for JSON output (null for console)</param>
    public PragmaticEnhancedJsonProvider(string name, IPragmaticProviderConfiguration configuration, string? filePath = null)
        : base(name, configuration)
    {
        _filePath = filePath;
        _jsonOptions = CreateJsonWriterOptions();
        _serializerOptions = CreateJsonSerializerOptions();

        _ndjsonWriter = new ThreadLocal<(ArrayBufferWriter<byte>, Utf8JsonWriter)>(() =>
        {
            var buffer = new ArrayBufferWriter<byte>(512);
            return (buffer, new Utf8JsonWriter(buffer, _jsonOptions));
        });

        // Read configuration
        _isNdjsonFormat = GetCustomProperty<bool>("EnableNDJSON", true);
        _enableAsyncBuffering = GetCustomProperty<bool>("EnableAsyncBuffering", true);
        _atomicWrites = GetCustomProperty<bool>("EnableAtomicWrites", true);

        ValidateConfiguration();

        if (_enableAsyncBuffering)
        {
            var flushInterval = TimeSpan.FromMilliseconds(
                GetCustomProperty<int>("AsyncFlushIntervalMs", 2000));

            // Use a synchronous TimerCallback that schedules the async work via Task.Run
            // to avoid async void — an unhandled exception in async void crashes the process
            _flushTimer = new Timer(
                _ => { _ = Task.Run(async () =>
                {
                    try { await FlushBufferAsync().ConfigureAwait(false); }
                    catch (Exception ex) { Console.Error.WriteLine($"[PragmaticEnhancedJsonProvider] Flush error: {ex}"); }
                }); },
                null,
                flushInterval,
                flushInterval);
        }

        // Initialize file if specified
        if (_filePath != null)
        {
            EnsureFileInitialized();
        }
    }

    /// <inheritdoc />
    protected override void WriteLogCore(LogEntry logEntry)
    {
        if (_enableAsyncBuffering)
        {
            // Queue for async processing
            _logQueue.Enqueue(logEntry);
            Interlocked.Increment(ref _pendingLogCount);

            // Trigger immediate flush if queue is getting full
            var maxQueueSize = GetCustomProperty<int>("MaxAsyncQueueSize", 1000);
            if (_pendingLogCount >= maxQueueSize)
            {
                _ = Task.Run(async () => await FlushBufferAsync());
            }
        }
        else
        {
            // Synchronous write
            WriteLogEntrySync(logEntry);
        }
    }

    /// <inheritdoc />
    protected override void DisposeCore()
    {
        // Stop the periodic flush timer FIRST so no new flush is scheduled while we drain.
        _flushTimer?.Dispose();

        // Flush any remaining buffered logs synchronously, but off any captured
        // SynchronizationContext to avoid a sync-over-async deadlock. Task.Run moves the
        // continuation onto the thread pool, so blocking here cannot deadlock the caller.
        // Drain happens before cancellation so the final flush is not aborted mid-write.
        if (_enableAsyncBuffering)
        {
            try
            {
                Task.Run(FlushBufferAsync).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PragmaticEnhancedJsonProvider] Dispose flush error: {ex}");
            }
        }

        _cancellationTokenSource.Cancel();
        _flushSemaphore.Dispose();
        _cancellationTokenSource.Dispose();
        _ndjsonWriter.Dispose();

        lock (_fileLock)
        {
            _streamWriter?.Dispose();
            _fileStream?.Dispose();
        }
    }

    /// <inheritdoc />
    protected override ProviderHealthStatus PerformCustomHealthCheck()
    {
        if (_filePath != null)
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (directory != null && !Directory.Exists(directory))
                {
                    return ProviderHealthStatus.Unhealthy;
                }

                // Check queue depth for async buffering
                if (_enableAsyncBuffering && _pendingLogCount > GetCustomProperty<int>("HealthCheckQueueThreshold", 5000))
                {
                    return ProviderHealthStatus.Degraded;
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
        return new Dictionary<string, object?>
        {
            ["OutputType"] = _filePath != null ? "File" : "Console",
            ["Format"] = _isNdjsonFormat ? "NDJSON" : "JSON",
            ["AsyncBuffering"] = _enableAsyncBuffering,
            ["AtomicWrites"] = _atomicWrites,
            ["PendingLogCount"] = _pendingLogCount,
            ["CurrentFileSize"] = _currentFileSize,
            ["TotalBytesWritten"] = _totalBytesWritten,
            ["TotalLogsProcessed"] = _totalLogsProcessed,
            ["LastFlushTime"] = _lastFlushTime,
            ["FilePath"] = _filePath,
            ["FileExists"] = _filePath != null ? File.Exists(_filePath) : null
        };
    }

    private async Task FlushBufferAsync()
    {
        if (!await _flushSemaphore.WaitAsync(100, _cancellationTokenSource.Token))
            return;

        try
        {
            var logsToProcess = new List<LogEntry>();
            var maxBatchSize = GetCustomProperty<int>("MaxFlushBatchSize", 100);

            // Dequeue logs for processing
            for (int i = 0; i < maxBatchSize && _logQueue.TryDequeue(out var logEntry); i++)
            {
                logsToProcess.Add(logEntry);
                Interlocked.Decrement(ref _pendingLogCount);
            }

            if (logsToProcess.Count == 0)
                return;

            if (_filePath != null)
            {
                await WriteLogsToFileAsync(logsToProcess);
            }
            else
            {
                await WriteLogsToConsoleAsync(logsToProcess);
            }

            _lastFlushTime = DateTime.UtcNow;
            Interlocked.Add(ref _totalLogsProcessed, logsToProcess.Count);
        }
        finally
        {
            _flushSemaphore.Release();
        }
    }

    private async Task WriteLogsToFileAsync(List<LogEntry> logs)
    {
        if (_filePath == null)
            return;

        // Check if file rolling is needed
        if (ShouldRollFile())
        {
            await RollFileAsync();
        }

        lock (_fileLock)
        {
            EnsureFileInitialized();
        }

        if (_atomicWrites)
        {
            await WriteLogsAtomicallyAsync(logs);
        }
        else
        {
            await WriteLogsDirectlyAsync(logs);
        }
    }

    private async Task WriteLogsAtomicallyAsync(List<LogEntry> logs)
    {
        if (_filePath == null || _streamWriter == null)
            return;

        // NDJSON is append-only: never read the destination while writing it. The previous
        // implementation read _filePath (via File.ReadAllLinesAsync) and then File.Move'd a temp
        // file over it — but EnsureFileInitialized keeps an open append handle on the same path.
        // Self-reading + overwriting that file threw IOException on dispose/flush and could leave
        // the file empty. Append the new logs through the already-open writer instead; this keeps
        // each flush atomic at the line level without a read-modify-rewrite cycle.
        lock (_fileLock)
        {
            EnsureFileInitialized();
        }

        if (_streamWriter == null)
            return;

        foreach (var log in logs)
        {
            var jsonLine = SerializeLogEntry(log);
            await _streamWriter.WriteLineAsync(jsonLine);

            var bytes = Encoding.UTF8.GetByteCount(jsonLine) + Environment.NewLine.Length;
            _currentFileSize += bytes;
            _totalBytesWritten += bytes;
        }

        await _streamWriter.FlushAsync();
    }

    private async Task WriteLogsDirectlyAsync(List<LogEntry> logs)
    {
        if (_streamWriter == null)
            return;

        foreach (var log in logs)
        {
            var jsonLine = SerializeLogEntry(log);
            await _streamWriter.WriteLineAsync(jsonLine);
            _currentFileSize += Encoding.UTF8.GetByteCount(jsonLine) + Environment.NewLine.Length;
            _totalBytesWritten += Encoding.UTF8.GetByteCount(jsonLine) + Environment.NewLine.Length;
        }

        await _streamWriter.FlushAsync();
    }

    private async Task WriteLogsToConsoleAsync(List<LogEntry> logs)
    {
        foreach (var log in logs)
        {
            var jsonLine = SerializeLogEntry(log);
            await Console.Out.WriteLineAsync(jsonLine);
            _totalBytesWritten += Encoding.UTF8.GetByteCount(jsonLine) + Environment.NewLine.Length;
        }

        await Console.Out.FlushAsync();
    }

    private void WriteLogEntrySync(LogEntry logEntry)
    {
        var jsonLine = SerializeLogEntry(logEntry);

        if (_filePath != null)
        {
            lock (_fileLock)
            {
                if (ShouldRollFile())
                {
                    RollFileSync();
                }

                EnsureFileInitialized();
                _streamWriter?.WriteLine(jsonLine);
                _streamWriter?.Flush();

                _currentFileSize += Encoding.UTF8.GetByteCount(jsonLine) + Environment.NewLine.Length;
            }
        }
        else
        {
            Console.WriteLine(jsonLine);
        }

        _totalBytesWritten += Encoding.UTF8.GetByteCount(jsonLine) + Environment.NewLine.Length;
        Interlocked.Increment(ref _totalLogsProcessed);
    }

    private string SerializeLogEntry(LogEntry logEntry)
    {
        if (_isNdjsonFormat)
        {
            // NDJSON: Single line JSON per log entry
            return SerializeToNdjson(logEntry);
        }
        else
        {
            // Traditional JSON array format (less common for streaming)
            return SerializeToJson(logEntry);
        }
    }

    private string SerializeToNdjson(LogEntry logEntry)
    {
        var (buffer, writer) = _ndjsonWriter.Value;
        buffer.Clear();
        writer.Reset(buffer);

        writer.WriteStartObject();

        // Standard fields with @ prefix for easy identification
        writer.WriteString("@timestamp", FormatTimestamp(logEntry.Timestamp));
        writer.WriteString("@level", GetLogLevelString(logEntry.LogLevel));
        writer.WriteString("@logger", logEntry.Category);
        writer.WriteString("@message", logEntry.Message);

        // Optional fields
        if (logEntry.EventId.Id != 0)
        {
            writer.WriteNumber("@eventId", logEntry.EventId.Id);
            if (!string.IsNullOrEmpty(logEntry.EventId.Name))
                writer.WriteString("@eventName", logEntry.EventId.Name);
        }

        if (logEntry.Exception != null)
        {
            WriteExceptionDetails(writer, "@exception", logEntry.Exception);
        }

        if (!string.IsNullOrEmpty(logEntry.MessageTemplate))
        {
            writer.WriteString("@messageTemplate", logEntry.MessageTemplate);
        }

        // Structured properties (flattened for better searchability)
        WriteStructuredProperties(writer, logEntry.Properties);

        // Scope information
        if (logEntry.Scopes?.Count > 0)
        {
            WriteScopeInformation(writer, logEntry.Scopes);
        }

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <remarks>
    ///     The fields an anonymous object used to carry, in the same camelCase names and with nulls left
    ///     out, written by hand: serializing an anonymous type reflects over it, which Native AOT
    ///     refuses. A property value is written the way the NDJSON form writes it, a complex one as its
    ///     JSON string.
    /// </remarks>
    private string SerializeToJson(LogEntry logEntry)
    {
        var (buffer, writer) = _ndjsonWriter.Value;
        buffer.Clear();
        writer.Reset(buffer);

        writer.WriteStartObject();
        writer.WriteString("timestamp", FormatTimestamp(logEntry.Timestamp));
        writer.WriteString("level", GetLogLevelString(logEntry.LogLevel));
        WriteStringIfPresent(writer, "category", logEntry.Category);
        WriteStringIfPresent(writer, "message", logEntry.Message);

        if (logEntry.EventId.Id != 0)
            writer.WriteNumber("eventId", logEntry.EventId.Id);
        if (!string.IsNullOrEmpty(logEntry.EventId.Name))
            writer.WriteString("eventName", logEntry.EventId.Name);

        WriteStringIfPresent(writer, "exception", logEntry.Exception?.ToString());
        WriteStringIfPresent(writer, "messageTemplate", logEntry.MessageTemplate);

        if (logEntry.Properties.Count > 0)
        {
            writer.WriteStartObject("properties");
            foreach (var kvp in logEntry.Properties)
                WriteJsonValue(writer, kvp.Key, kvp.Value);
            writer.WriteEndObject();
        }

        if (logEntry.Scopes?.Count > 0)
        {
            writer.WriteStartArray("scopes");
            foreach (var scope in logEntry.Scopes)
            {
                writer.WriteStartObject();
                writer.WriteString("key", scope.Key);
                WriteJsonValue(writer, "value", scope.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteStringIfPresent(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
            writer.WriteString(name, value);
    }

    // File management methods (enhanced versions from original)
    private bool ShouldRollFile()
    {
        if (_filePath == null)
            return false;

        var maxFileSize = GetCustomProperty<long>("MaxFileSizeBytes", 100 * 1024 * 1024);
        var rollByDate = GetCustomProperty<bool>("RollByDate", true);
        var rollByHour = GetCustomProperty<bool>("RollByHour", false);

        // Size-based rolling
        if (maxFileSize > 0 && _currentFileSize >= maxFileSize)
            return true;

        // Time-based rolling
        if (rollByDate)
        {
            var now = DateTime.Now;
            if (rollByHour)
            {
                return _currentFileDate.Date != now.Date || _currentFileDate.Hour != now.Hour;
            }
            else
            {
                return _currentFileDate.Date != now.Date;
            }
        }

        return false;
    }

    private async Task RollFileAsync()
    {
        if (_filePath == null)
            return;

        await _flushSemaphore.WaitAsync(_cancellationTokenSource.Token);
        try
        {
            lock (_fileLock)
            {
                RollFileSync();
            }
        }
        finally
        {
            _flushSemaphore.Release();
        }
    }

    private void RollFileSync()
    {
        if (_filePath == null)
            return;

        _streamWriter?.Dispose();
        _fileStream?.Dispose();

        var directory = Path.GetDirectoryName(_filePath) ?? "";
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(_filePath);
        var extension = Path.GetExtension(_filePath);

        var rollStrategy = GetCustomProperty<string>("RollStrategy", "timestamp");
        string rolledFileName;

        if (rollStrategy.Equals("sequential", StringComparison.OrdinalIgnoreCase))
        {
            // Sequential numbering: log_001.json, log_002.json, etc.
            var counter = 1;
            do
            {
                rolledFileName = $"{fileNameWithoutExt}_{counter:D3}{extension}";
                counter++;
            } while (File.Exists(Path.Combine(directory, rolledFileName)));
        }
        else
        {
            // Timestamp-based (default): log_20250109-143022.json
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            rolledFileName = $"{fileNameWithoutExt}_{timestamp}{extension}";
        }

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

    private void EnsureFileInitialized()
    {
        if (_filePath == null || (_fileStream != null && _streamWriter != null))
            return;

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _fileStream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, true);
        _streamWriter = new StreamWriter(_fileStream, LogTextEncoding.Utf8);

        if (_currentFileDate == DateTime.MinValue)
        {
            _currentFileDate = DateTime.Now;
            if (File.Exists(_filePath))
            {
                _currentFileSize = new FileInfo(_filePath).Length;
            }
        }
    }

    // Helper methods (similar to original but optimized)
    private void WriteExceptionDetails(Utf8JsonWriter writer, string propertyName, Exception exception)
    {
        writer.WriteStartObject(propertyName);
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
                    WriteJsonValue(writer, key.ToString()!, exception.Data[key]);
                }
            }
            writer.WriteEndObject();
        }

        if (exception.InnerException != null)
        {
            WriteExceptionDetails(writer, "innerException", exception.InnerException);
        }

        writer.WriteEndObject();
    }

    private void WriteStructuredProperties(Utf8JsonWriter writer, Dictionary<string, object?> properties)
    {
        if (properties.Count == 0)
            return;

        // In NDJSON, flatten properties to root level with prefix
        var flattenProperties = GetCustomProperty<bool>("FlattenProperties", true);

        if (flattenProperties && _isNdjsonFormat)
        {
            foreach (var kvp in properties)
            {
                WriteJsonValue(writer, $"prop_{kvp.Key}", kvp.Value);
            }
        }
        else
        {
            writer.WriteStartObject("@properties");
            foreach (var kvp in properties)
            {
                WriteJsonValue(writer, kvp.Key, kvp.Value);
            }
            writer.WriteEndObject();
        }
    }

    private void WriteScopeInformation(Utf8JsonWriter writer, IReadOnlyList<KeyValuePair<string, object?>> scopes)
    {
        writer.WriteStartArray("@scopes");
        foreach (var scope in scopes)
        {
            writer.WriteStartObject();
            WriteJsonValue(writer, scope.Key, scope.Value);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private void WriteJsonValue(Utf8JsonWriter writer, string propertyName, object? value)
    {
        writer.WritePropertyName(propertyName);
        WriteJsonValueDirect(writer, value);
    }

    private void WriteJsonValueDirect(Utf8JsonWriter writer, object? value)
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
            case DateTime dateTimeValue:
                writer.WriteStringValue(dateTimeValue.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset dateTimeOffsetValue:
                writer.WriteStringValue(dateTimeOffsetValue.ToString("O", CultureInfo.InvariantCulture));
                break;
            case TimeSpan timeSpanValue:
                writer.WriteStringValue(timeSpanValue.ToString("c", CultureInfo.InvariantCulture));
                break;
            case Guid guidValue:
                writer.WriteStringValue(guidValue.ToString("D", CultureInfo.InvariantCulture));
                break;
            case string stringValue:
                writer.WriteStringValue(stringValue);
                break;
            default:
                // Declared redaction ([NotLogged] / [PersonalData]) already happened on the entry,
                // in PragmaticLoggerProviderBase — one place, every provider, no flag.
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
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRITICAL",
        _ => "UNKNOWN"
    };

    private JsonWriterOptions CreateJsonWriterOptions()
    {
        return new JsonWriterOptions
        {
            Indented = false, // NDJSON is always single-line
            SkipValidation = GetCustomProperty<bool>("SkipValidation", true),
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    private JsonSerializerOptions CreateJsonSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = false,
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

    private void ValidateConfiguration()
    {
        if (_enableAsyncBuffering && !Configuration.Performance.EnableBatching)
        {
            throw new ArgumentException("Async buffering requires batching to be enabled for optimal performance.");
        }
    }
}

/// <summary>
/// Enhanced configuration for the JSON provider with NDJSON and async support.
/// </summary>
public static class PragmaticEnhancedJsonConfiguration
{
    /// <summary>
    /// Creates optimal configuration for NDJSON with async buffering.
    /// </summary>
    public static PragmaticProviderConfiguration ForNdjsonAsync()
    {
        var config = new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Information,
            IncludeStructuredProperties = true,
            IncludeContextEnrichment = true,
            ContextFilter = new ContextFilterConfiguration
            {
                Mode = ContextFilterMode.Exclude,
                PropertyNames = new HashSet<string> { "MachineName", "ProcessId" }
            },
            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ",
                UseUtcTimestamp = true,
                IncludeExceptionDetails = true,
                MaxMessageLength = 0
            },
            Performance = new PerformanceConfiguration
            {
                EnableBatching = true,
                BatchSize = 100,
                FlushInterval = TimeSpan.FromSeconds(2),
                UseZeroAllocation = true,
                MaxQueueSize = 10000,
                OverflowStrategy = QueueOverflowStrategy.DropOldest
            }
        };

        // NDJSON-specific properties
        config.CustomProperties["EnableNDJSON"] = true;
        config.CustomProperties["EnableAsyncBuffering"] = true;
        config.CustomProperties["EnableAtomicWrites"] = true;
        config.CustomProperties["FlattenProperties"] = true;
        config.CustomProperties["SerializeComplexObjects"] = true;
        config.CustomProperties["SkipValidation"] = true;

        // Async buffering configuration
        config.CustomProperties["AsyncFlushIntervalMs"] = 2000;
        config.CustomProperties["MaxAsyncQueueSize"] = 1000;
        config.CustomProperties["MaxFlushBatchSize"] = 100;
        config.CustomProperties["HealthCheckQueueThreshold"] = 5000;

        // File rolling configuration
        config.CustomProperties["MaxFileSizeBytes"] = 500L * 1024 * 1024; // 500MB
        config.CustomProperties["RollByDate"] = true;
        config.CustomProperties["RollByHour"] = false;
        config.CustomProperties["RollStrategy"] = "timestamp"; // or "sequential"

        return config;
    }

    /// <summary>
    /// Creates configuration for high-performance file logging with atomic writes.
    /// </summary>
    public static PragmaticProviderConfiguration ForHighPerformanceFile()
    {
        var config = ForNdjsonAsync();

        // Override for file-specific optimizations
        config.Performance.BatchSize = 200;
        config.Performance.FlushInterval = TimeSpan.FromSeconds(5);
        config.Performance.MaxQueueSize = 20000;

        // Enhanced atomic write settings
        config.CustomProperties["EnableAtomicWrites"] = true;
        config.CustomProperties["MaxFileSizeBytes"] = 1000L * 1024 * 1024; // 1GB
        config.CustomProperties["AsyncFlushIntervalMs"] = 5000;
        config.CustomProperties["MaxFlushBatchSize"] = 200;

        return config;
    }

    /// <summary>
    /// Creates configuration for real-time streaming (minimal buffering).
    /// </summary>
    public static PragmaticProviderConfiguration ForRealTimeStreaming()
    {
        var config = ForNdjsonAsync();

        // Minimal buffering for real-time scenarios
        config.Performance.BatchSize = 10;
        config.Performance.FlushInterval = TimeSpan.FromMilliseconds(100);

        config.CustomProperties["AsyncFlushIntervalMs"] = 100;
        config.CustomProperties["MaxAsyncQueueSize"] = 50;
        config.CustomProperties["MaxFlushBatchSize"] = 10;
        config.CustomProperties["EnableAtomicWrites"] = false; // Disable for speed

        return config;
    }
}