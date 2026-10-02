using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Providers;

/// <summary>
/// Represents a log entry with all information needed for provider processing.
/// </summary>
public sealed class LogEntry
{
    // Lazy backing fields: most log entries carry no structured properties/metadata, and
    // allocating two dictionaries per log call dominated the hot path. Readers that only
    // need to know "is there anything?" must use HasProperties/HasMetadata (no allocation).
    private Dictionary<string, object?>? _properties;
    private Dictionary<string, object?>? _metadata;

    /// <summary>
    /// Initializes a new instance of the LogEntry class.
    /// </summary>
    public LogEntry()
    {
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets or sets the timestamp when the log entry was created.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the log level.
    /// </summary>
    public LogLevel LogLevel { get; set; }

    /// <summary>
    /// Gets or sets the event ID.
    /// </summary>
    public EventId EventId { get; set; }

    /// <summary>
    /// Gets or sets the category name (usually the logger name).
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the formatted log message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the original message template.
    /// </summary>
    public string? MessageTemplate { get; set; }

    /// <summary>
    /// Gets or sets the message parameters.
    /// </summary>
    public object?[]? Parameters { get; set; }

    /// <summary>
    /// Gets or sets the exception associated with this log entry.
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Gets or sets the structured properties for this log entry.
    /// The dictionary is created lazily on first access; use <see cref="HasProperties" />
    /// to check for content without allocating.
    /// </summary>
    public Dictionary<string, object?> Properties
    {
        get => _properties ??= new Dictionary<string, object?>();
        set => _properties = value;
    }

    /// <summary>
    /// Gets a value indicating whether this entry has any structured properties
    /// (without allocating the backing dictionary).
    /// </summary>
    public bool HasProperties => _properties is { Count: > 0 };

    /// <summary>
    /// Gets or sets the scope information.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, object?>>? Scopes { get; set; }

    /// <summary>
    /// Gets or sets additional metadata for this log entry.
    /// The dictionary is created lazily on first access; use <see cref="HasMetadata" />
    /// to check for content without allocating.
    /// </summary>
    public Dictionary<string, object?> Metadata
    {
        get => _metadata ??= new Dictionary<string, object?>();
        set => _metadata = value;
    }

    /// <summary>
    /// Gets a value indicating whether this entry has any metadata
    /// (without allocating the backing dictionary).
    /// </summary>
    public bool HasMetadata => _metadata is { Count: > 0 };

    /// <summary>
    /// Creates a copy of this log entry.
    /// </summary>
    /// <returns>A new LogEntry with the same data</returns>
    public LogEntry Clone()
    {
        var clone = new LogEntry
        {
            Timestamp = Timestamp,
            LogLevel = LogLevel,
            EventId = EventId,
            Category = Category,
            Message = Message,
            MessageTemplate = MessageTemplate,
            Parameters = Parameters?.ToArray(),
            Exception = Exception,
            Scopes = Scopes
        };

        if (_properties is not null)
            clone._properties = new Dictionary<string, object?>(_properties);
        if (_metadata is not null)
            clone._metadata = new Dictionary<string, object?>(_metadata);

        return clone;
    }

    /// <summary>
    /// Gets the log level as a string.
    /// </summary>
    /// <returns>String representation of the log level</returns>
    public string GetLogLevelString()
    {
        return LogLevel switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "FATAL",
            _ => LogLevel.ToString().ToUpperInvariant()
        };
    }

    /// <summary>
    /// Gets a short log level string (4 characters max).
    /// </summary>
    /// <returns>Short string representation of the log level</returns>
    public string GetShortLogLevelString()
    {
        return LogLevel switch
        {
            LogLevel.Trace => "TRCE",
            LogLevel.Debug => "DBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "FAIL",
            LogLevel.Critical => "CRIT",
            _ => LogLevel.ToString().ToUpperInvariant().Substring(0, Math.Min(4, LogLevel.ToString().Length))
        };
    }

    /// <summary>
    /// Formats the timestamp using the specified format.
    /// </summary>
    /// <param name="format">The timestamp format</param>
    /// <param name="useUtc">Whether to use UTC time</param>
    /// <returns>Formatted timestamp string</returns>
    public string FormatTimestamp(string format, bool useUtc = true)
    {
        var timestamp = useUtc ? Timestamp : Timestamp.ToLocalTime();
        return timestamp.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets all properties combined (structured properties + scopes).
    /// </summary>
    /// <returns>Combined properties dictionary</returns>
    public Dictionary<string, object?> GetAllProperties()
    {
        var allProperties = _properties is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(_properties);

        // Add scope properties
        if (Scopes != null)
        {
            foreach (var kvp in Scopes)
            {
                // Scope properties don't override structured properties
                if (!allProperties.ContainsKey(kvp.Key))
                {
                    allProperties[kvp.Key] = kvp.Value;
                }
            }
        }

        return allProperties;
    }
}