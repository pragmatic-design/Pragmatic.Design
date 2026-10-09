using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Providers;

/// <summary>
///     One log call as a provider writes it: the level, the event, the category, the rendered message, the structured
///     properties, the ambient scopes, the exception.
/// </summary>
/// <remarks>
///     <para>
///         Read from the call's state where it is — the properties copied out of it into a list the thread reuses,
///         the scopes from the ambient scope stack — so a call builds no <see cref="LogEntry" /> and no dictionary.
///         A call that needs the entry (an advanced filter, the pattern redactor) still builds one, and reaches the
///         provider as an event read from it. Either way the provider writes from this type, once.
///     </para>
///     <para>
///         ⚠️ Valid only while the provider writes it: the next call on the thread reuses it. A provider that keeps
///         the event — a queue, a buffer, an in-memory store — keeps <see cref="ToEntry" />, which copies it.
///     </para>
/// </remarks>
public sealed class LogEvent
{
    /// <summary>When the call was made.</summary>
    public DateTime Timestamp { get; private set; }

    /// <summary>The level of the call.</summary>
    public LogLevel LogLevel { get; private set; }

    /// <summary>The event of the call.</summary>
    public EventId EventId { get; private set; }

    /// <summary>The category, usually the logger's name.</summary>
    public string Category { get; private set; } = string.Empty;

    /// <summary>The rendered message.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>The message template, when the call had one.</summary>
    public string? MessageTemplate { get; private set; }

    /// <summary>The exception the call carries.</summary>
    public Exception? Exception { get; private set; }

    /// <summary>The structured properties: the call's own, then the context's when it is enriched.</summary>
    public LogEventValues Properties { get; } = new();

    /// <summary>Whether there is any structured property.</summary>
    public bool HasProperties => Properties.Count > 0;

    /// <summary>The ambient scopes' properties, innermost scope first; a key may repeat across scopes.</summary>
    public LogEventValues Scopes { get; } = new();

    /// <summary>
    ///     Whether the instance is being written; a provider that logs while it writes gets another instance rather
    ///     than overwriting the one it is reading.
    /// </summary>
    internal bool InUse { get; set; }

    internal void Start(
        DateTime timestamp, LogLevel logLevel, EventId eventId, string category, string message, string? template, Exception? exception)
    {
        Timestamp = timestamp;
        LogLevel = logLevel;
        EventId = eventId;
        Category = category;
        Message = message;
        MessageTemplate = template;
        Exception = exception;
        Properties.Clear();
        Scopes.Clear();
    }

    internal void SetTemplate(string? template) => MessageTemplate = template;

    /// <summary>The event read from an entry, for a call that had to build one.</summary>
    internal void From(LogEntry entry)
    {
        Start(entry.Timestamp, entry.LogLevel, entry.EventId, entry.Category, entry.Message, entry.MessageTemplate, entry.Exception);
        if (entry.HasProperties)
        {
            foreach (var property in entry.Properties)
                Properties.Append(property.Key, property.Value);
        }

        if (entry.Scopes is { } scopes)
        {
            foreach (var scope in scopes)
                Scopes.Append(scope.Key, scope.Value);
        }
    }

    /// <summary>Drops the references the call held, so a reused instance keeps nothing alive.</summary>
    internal void Release()
    {
        Message = string.Empty;
        MessageTemplate = null;
        Exception = null;
        Properties.Clear();
        Scopes.Clear();
        InUse = false;
    }

    /// <summary>A copy of the event that outlives the call, for a provider that keeps it.</summary>
    public LogEntry ToEntry()
    {
        var entry = new LogEntry
        {
            Timestamp = Timestamp,
            LogLevel = LogLevel,
            EventId = EventId,
            Category = Category,
            Message = Message,
            MessageTemplate = MessageTemplate,
            Exception = Exception,
        };

        if (Properties.Count > 0)
        {
            var properties = entry.Properties;
            foreach (var property in Properties)
                properties[property.Key] = property.Value;
        }

        if (Scopes.Count > 0)
            entry.Scopes = [.. Scopes];

        return entry;
    }

    /// <summary>The level as a word: TRACE, DEBUG, INFO, WARN, ERROR, FATAL.</summary>
    public string GetLogLevelString() => LogLevel switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "FATAL",
        _ => LogLevel.ToString().ToUpperInvariant(),
    };

    /// <summary>The level in four characters at most: TRCE, DBUG, INFO, WARN, FAIL, CRIT.</summary>
    public string GetShortLogLevelString() => LogLevel switch
    {
        LogLevel.Trace => "TRCE",
        LogLevel.Debug => "DBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "FAIL",
        LogLevel.Critical => "CRIT",
        _ => LogLevel.ToString().ToUpperInvariant()[..Math.Min(4, LogLevel.ToString().Length)],
    };

    /// <summary>The timestamp in <paramref name="format" />, in UTC or local time.</summary>
    public string FormatTimestamp(string format, bool useUtc = true)
        => (useUtc ? Timestamp : Timestamp.ToLocalTime()).ToString(format, CultureInfo.InvariantCulture);
}
