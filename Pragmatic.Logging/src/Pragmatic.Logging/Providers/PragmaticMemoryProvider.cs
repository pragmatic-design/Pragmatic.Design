using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Providers;

/// <summary>
/// Memory logging provider that stores log entries in memory for testing and debugging.
/// Thread-safe and provides methods to inspect, filter, and clear logged entries.
/// </summary>
public sealed class PragmaticMemoryProvider : PragmaticLoggerProviderBase
{
    private readonly ConcurrentQueue<LogEntry> _logEntries = new();
    private readonly object _statsLock = new();
    private volatile int _maxEntries;
    private volatile bool _autoTruncate;

    // Statistics
    private long _totalEntriesReceived;
    private long _entriesTruncated;

    /// <summary>
    /// Initializes a new instance of the Memory provider.
    /// </summary>
    /// <param name="name">Provider name</param>
    /// <param name="configuration">Provider configuration</param>
    public PragmaticMemoryProvider(string name, IPragmaticProviderConfiguration configuration)
        : base(name, configuration)
    {
        ValidateMemoryConfiguration();
        _maxEntries = GetCustomProperty<int>("MaxEntries", 10000);
        _autoTruncate = GetCustomProperty<bool>("AutoTruncate", true);
    }

    /// <summary>
    /// Gets the current count of stored log entries.
    /// </summary>
    public int Count => _logEntries.Count;

    /// <summary>
    /// Gets all stored log entries as a read-only list.
    /// </summary>
    /// <returns>A snapshot of all log entries</returns>
    public IReadOnlyList<LogEntry> GetLogEntries()
    {
        return _logEntries.ToArray();
    }

    /// <summary>
    /// Gets log entries that match the specified predicate.
    /// </summary>
    /// <param name="predicate">Filter predicate</param>
    /// <returns>Filtered log entries</returns>
    public IReadOnlyList<LogEntry> GetLogEntries(Func<LogEntry, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return _logEntries.Where(predicate).ToArray();
    }

    /// <summary>
    /// Gets log entries for a specific log level.
    /// </summary>
    /// <param name="logLevel">Log level to filter by</param>
    /// <returns>Log entries at the specified level</returns>
    public IReadOnlyList<LogEntry> GetLogEntries(LogLevel logLevel)
    {
        return GetLogEntries(entry => entry.LogLevel == logLevel);
    }

    /// <summary>
    /// Gets log entries for a specific category.
    /// </summary>
    /// <param name="category">Category to filter by</param>
    /// <returns>Log entries from the specified category</returns>
    public IReadOnlyList<LogEntry> GetLogEntries(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        return GetLogEntries(entry => string.Equals(entry.Category, category, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets log entries containing the specified message text.
    /// </summary>
    /// <param name="messageText">Text to search for in messages</param>
    /// <param name="ignoreCase">Whether to ignore case when searching</param>
    /// <returns>Log entries containing the specified text</returns>
    public IReadOnlyList<LogEntry> GetLogEntriesContaining(string messageText, bool ignoreCase = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageText);
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return GetLogEntries(entry => entry.Message.Contains(messageText, comparison));
    }

    /// <summary>
    /// Gets the most recent log entries up to the specified count.
    /// </summary>
    /// <param name="count">Maximum number of entries to return</param>
    /// <returns>Most recent log entries</returns>
    public IReadOnlyList<LogEntry> GetLatestLogEntries(int count)
    {
        if (count <= 0)
            return Array.Empty<LogEntry>();

        return _logEntries.ToArray()
            .OrderByDescending(entry => entry.Timestamp)
            .Take(count)
            .ToArray();
    }

    /// <summary>
    /// Gets formatted log messages as strings.
    /// </summary>
    /// <param name="includeTimestamp">Whether to include timestamp in formatted output</param>
    /// <returns>Formatted log messages</returns>
    public IReadOnlyList<string> GetFormattedMessages(bool includeTimestamp = true)
    {
        return _logEntries.Select(entry => FormatLogEntry(entry, includeTimestamp)).ToArray();
    }

    /// <summary>
    /// Clears all stored log entries.
    /// </summary>
    public void Clear()
    {
        _logEntries.Clear();
    }

    /// <summary>
    /// Checks if any log entries match the specified predicate.
    /// </summary>
    /// <param name="predicate">Predicate to test</param>
    /// <returns>True if any entries match</returns>
    public bool HasLogEntry(Func<LogEntry, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return _logEntries.Any(predicate);
    }

    /// <summary>
    /// Checks if there are any log entries at the specified level.
    /// </summary>
    /// <param name="logLevel">Log level to check</param>
    /// <returns>True if there are entries at this level</returns>
    public bool HasLogEntry(LogLevel logLevel)
    {
        return HasLogEntry(entry => entry.LogLevel == logLevel);
    }

    /// <summary>
    /// Checks if there are any log entries containing the specified text.
    /// </summary>
    /// <param name="messageText">Text to search for</param>
    /// <param name="ignoreCase">Whether to ignore case</param>
    /// <returns>True if any entries contain the text</returns>
    public bool HasLogEntryContaining(string messageText, bool ignoreCase = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageText);
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return HasLogEntry(entry => entry.Message.Contains(messageText, comparison));
    }

    /// <inheritdoc />
    protected override void WriteLogCore(LogEvent logEvent)
    {
        // The event is reused by the next call: the store keeps a copy.
        var entryCopy = logEvent.ToEntry();

        _logEntries.Enqueue(entryCopy);

        lock (_statsLock)
        {
            _totalEntriesReceived++;
        }

        // Auto-truncate if enabled and limit exceeded
        if (_autoTruncate && _logEntries.Count > _maxEntries)
        {
            TruncateEntries();
        }
    }

    /// <inheritdoc />
    protected override ProviderHealthStatus PerformCustomHealthCheck()
    {
        var count = _logEntries.Count;
        var maxEntries = _maxEntries;

        // Health based on memory usage
        if (count > maxEntries * 0.9) // More than 90% full
            return ProviderHealthStatus.Warning;
        if (count > maxEntries) // Over limit
            return ProviderHealthStatus.Degraded;

        return ProviderHealthStatus.Healthy;
    }

    /// <inheritdoc />
    protected override Dictionary<string, object?> GetCustomMetrics()
    {
        lock (_statsLock)
        {
            return new Dictionary<string, object?>
            {
                ["CurrentEntries"] = _logEntries.Count,
                ["MaxEntries"] = _maxEntries,
                ["TotalEntriesReceived"] = _totalEntriesReceived,
                ["EntriesTruncated"] = _entriesTruncated,
                ["AutoTruncate"] = _autoTruncate,
                ["MemoryUsageEstimateKB"] = EstimateMemoryUsageKB(),
                ["OldestEntryAge"] = GetOldestEntryAge(),
                ["NewestEntryAge"] = GetNewestEntryAge()
            };
        }
    }

    /// <inheritdoc />
    protected override void OnConfigurationUpdated(IPragmaticProviderConfiguration oldConfig, IPragmaticProviderConfiguration newConfig)
    {
        _maxEntries = GetCustomProperty<int>("MaxEntries", 10000);
        _autoTruncate = GetCustomProperty<bool>("AutoTruncate", true);

        // Truncate immediately if the new limit is lower
        if (_autoTruncate && _logEntries.Count > _maxEntries)
        {
            TruncateEntries();
        }
    }

    private void ValidateMemoryConfiguration()
    {
        var maxEntries = GetCustomProperty<int>("MaxEntries", 10000);
        if (maxEntries <= 0)
        {
            throw new ArgumentException("MaxEntries must be greater than 0 for Memory provider.");
        }

        if (maxEntries > 1000000) // 1 million entries limit for safety
        {
            throw new ArgumentException("MaxEntries cannot exceed 1,000,000 for Memory provider to prevent excessive memory usage.");
        }
    }

    private void TruncateEntries()
    {
        var targetCount = _maxEntries / 2; // Remove half when truncating
        var currentCount = _logEntries.Count;
        var toRemove = currentCount - targetCount;

        if (toRemove <= 0)
            return;

        lock (_statsLock)
        {
            for (int i = 0; i < toRemove && _logEntries.TryDequeue(out _); i++)
            {
                _entriesTruncated++;
            }
        }
    }

    private long EstimateMemoryUsageKB()
    {
        // Rough estimate: each log entry is approximately 500 bytes on average
        // (includes message, properties, metadata, etc.)
        const int avgBytesPerEntry = 500;
        return (_logEntries.Count * avgBytesPerEntry) / 1024;
    }

    private TimeSpan? GetOldestEntryAge()
    {
        var entries = _logEntries.ToArray();
        if (entries.Length == 0)
            return null;

        var oldest = entries.Min(e => e.Timestamp);
        return DateTime.UtcNow - oldest;
    }

    private TimeSpan? GetNewestEntryAge()
    {
        var entries = _logEntries.ToArray();
        if (entries.Length == 0)
            return null;

        var newest = entries.Max(e => e.Timestamp);
        return DateTime.UtcNow - newest;
    }

    private string FormatLogEntry(LogEntry logEntry, bool includeTimestamp)
    {
        var sb = new StringBuilder();

        if (includeTimestamp)
        {
            sb.Append('[');
            sb.Append(logEntry.FormatTimestamp(Configuration.Formatting.TimestampFormat, Configuration.Formatting.UseUtcTimestamp));
            sb.Append("] ");
        }

        sb.Append('[');
        sb.Append(logEntry.GetShortLogLevelString());
        sb.Append("] ");

        sb.Append(logEntry.Category);
        sb.Append(": ");
        sb.Append(logEntry.Message);

        if (logEntry.Exception != null)
        {
            sb.AppendLine();
            sb.Append("Exception: ");
            sb.Append(logEntry.Exception.ToString());
        }

        return sb.ToString();
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
/// Configuration extensions for Memory provider.
/// </summary>
public static class PragmaticMemoryConfiguration
{
    /// <summary>
    /// Creates an optimal configuration for memory-based logging (testing/debugging).
    /// </summary>
    /// <returns>Memory-optimized configuration</returns>
    public static PragmaticProviderConfiguration ForMemory()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Trace, // Capture everything for debugging
            IncludeStructuredProperties = true,
            IncludeContextEnrichment = true,
            ContextFilter =
            {
                Mode = ContextFilterMode.All // Include all context for debugging
            },
            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "HH:mm:ss.fff",
                UseUtcTimestamp = false, // Local time for debugging
                MessageTemplate = "[{Timestamp}] [{Level}] {Category}: {Message}",
                IncludeExceptionDetails = true,
                MaxMessageLength = 0 // No limit for memory storage
            },
            Performance = new PerformanceConfiguration
            {
                EnableBatching = false, // Immediate logging for debugging
                UseZeroAllocation = false, // Prioritize functionality over performance
                MaxQueueSize = 50000 // Large queue for memory provider
            },
            CustomProperties = new Dictionary<string, object?>
            {
                ["MaxEntries"] = 10000,
                ["AutoTruncate"] = true
            }
        };
    }

    /// <summary>
    /// Creates a configuration for high-capacity memory logging.
    /// </summary>
    /// <returns>High-capacity memory configuration</returns>
    public static PragmaticProviderConfiguration ForHighCapacityMemory()
    {
        var config = ForMemory();
        config.CustomProperties["MaxEntries"] = 100000; // Higher capacity
        config.CustomProperties["AutoTruncate"] = true;
        return config;
    }

    /// <summary>
    /// Creates a configuration for test scenarios with minimal memory usage.
    /// </summary>
    /// <returns>Test-optimized memory configuration</returns>
    public static PragmaticProviderConfiguration ForTesting()
    {
        var config = ForMemory();
        config.MinimumLevel = LogLevel.Information; // Less verbose for tests
        config.IncludeContextEnrichment = false; // Reduce noise in tests
        config.CustomProperties["MaxEntries"] = 1000; // Smaller capacity for tests
        config.CustomProperties["AutoTruncate"] = true;
        return config;
    }
}