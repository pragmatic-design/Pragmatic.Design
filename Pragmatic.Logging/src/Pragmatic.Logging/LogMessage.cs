using Microsoft.Extensions.Logging;
using Pragmatic.Logging.ZeroAllocation;

namespace Pragmatic.Logging;

/// <summary>
/// Represents a structured log message with compile-time type safety and zero-allocation formatting.
/// This struct provides high-performance logging with compile-time validation and zero-allocation hot paths.
/// </summary>
/// <typeparam name="T">The type containing the log message properties</typeparam>
/// <example>
/// Basic usage with anonymous properties:
/// <code>
/// var message = LogMessage.Create(LogLevel.Information, 
///     "User {UserId} logged in from {IPAddress}", 
///     new { UserId = "john.doe", IPAddress = "192.168.1.1" });
/// </code>
/// 
/// Factory method usage:
/// <code>
/// var infoMessage = LogMessage.Information("Order {OrderId} processed", new { OrderId = 12345 });
/// var errorMessage = LogMessage.Error("Processing failed for {OrderId}", new { OrderId = 12345 }, exception: ex);
/// </code>
/// 
/// Zero-allocation formatting:
/// <code>
/// Span&lt;char&gt; buffer = stackalloc char[256];
/// if (message.TryFormat(buffer, out var written))
/// {
///     Console.WriteLine(buffer[..written]);
/// }
/// </code>
/// </example>
public readonly struct LogMessage<T> : ILogMessage<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LogMessage{T}"/> struct.
    /// </summary>
    /// <param name="logLevel">The log level</param>
    /// <param name="template">The message template</param>
    /// <param name="properties">The structured properties</param>
    /// <param name="eventId">The event identifier</param>
    /// <param name="exception">The exception, if any</param>
    public LogMessage(
        LogLevel logLevel,
        string template,
        T properties,
        EventId eventId = default,
        Exception? exception = null)
    {
        LogLevel = logLevel;
        Template = template ?? throw new ArgumentNullException(nameof(template));
        Properties = properties;
        EventId = eventId;
        Exception = exception;
    }

    /// <inheritdoc />
    public LogLevel LogLevel { get; }

    /// <inheritdoc />
    public string Template { get; }

    /// <inheritdoc />
    public T Properties { get; }

    /// <inheritdoc />
    public EventId EventId { get; }

    /// <inheritdoc />
    public Exception? Exception { get; }

    /// <summary>
    /// Formats the message using named property substitution from the Properties object.
    /// </summary>
    /// <returns>The formatted message string</returns>
    public override string ToString()
    {
        if (Properties == null)
            return Template;
        // NOTE: the (object) cast boxes when T is a value type. This is unavoidable here because
        // ZeroAllocMessageFormatter resolves properties via the IReadOnlyList/IReadOnlyDictionary
        // interfaces, which require an object reference for dispatch. In practice Properties is
        // almost always a reference type (anonymous type / M.E.L state), so no box occurs; struct
        // payloads pay one box per format call. A typed accessor path is future work.
        return ZeroAllocMessageFormatter.Format(Template.AsSpan(), (object)Properties);
    }

    /// <summary>
    /// Formats the message into a provided span using named property substitution.
    /// </summary>
    /// <param name="destination">The destination span to write to</param>
    /// <param name="charsWritten">The number of characters written</param>
    /// <returns>True if the message was successfully formatted, false if the span was too small</returns>
    public bool TryFormat(Span<char> destination, out int charsWritten)
    {
        if (Properties == null)
        {
            if (Template.Length > destination.Length)
            { charsWritten = 0; return false; }
            Template.AsSpan().CopyTo(destination);
            charsWritten = Template.Length;
            return true;
        }
        return ZeroAllocMessageFormatter.TryFormat(Template.AsSpan(), (object)Properties, destination, out charsWritten);
    }
}

/// <summary>
/// Provides factory methods for creating log messages.
/// </summary>
public static class LogMessage
{
    /// <summary>
    /// Creates a new log message with the specified parameters.
    /// </summary>
    /// <typeparam name="T">The type of the properties</typeparam>
    /// <param name="logLevel">The log level</param>
    /// <param name="template">The message template</param>
    /// <param name="properties">The structured properties</param>
    /// <param name="eventId">The event identifier</param>
    /// <param name="exception">The exception, if any</param>
    /// <returns>A new log message</returns>
    public static LogMessage<T> Create<T>(
        LogLevel logLevel,
        string template,
        T properties,
        EventId eventId = default,
        Exception? exception = null)
    {
        return new LogMessage<T>(logLevel, template, properties, eventId, exception);
    }

    /// <summary>
    /// Creates a new information-level log message.
    /// </summary>
    /// <typeparam name="T">The type of the properties</typeparam>
    /// <param name="template">The message template</param>
    /// <param name="properties">The structured properties</param>
    /// <param name="eventId">The event identifier</param>
    /// <returns>A new information log message</returns>
    public static LogMessage<T> Information<T>(
        string template,
        T properties,
        EventId eventId = default)
    {
        return new LogMessage<T>(LogLevel.Information, template, properties, eventId);
    }

    /// <summary>
    /// Creates a new warning-level log message.
    /// </summary>
    /// <typeparam name="T">The type of the properties</typeparam>
    /// <param name="template">The message template</param>
    /// <param name="properties">The structured properties</param>
    /// <param name="eventId">The event identifier</param>
    /// <returns>A new warning log message</returns>
    public static LogMessage<T> Warning<T>(
        string template,
        T properties,
        EventId eventId = default)
    {
        return new LogMessage<T>(LogLevel.Warning, template, properties, eventId);
    }

    /// <summary>
    /// Creates a new error-level log message.
    /// </summary>
    /// <typeparam name="T">The type of the properties</typeparam>
    /// <param name="template">The message template</param>
    /// <param name="properties">The structured properties</param>
    /// <param name="eventId">The event identifier</param>
    /// <param name="exception">The exception</param>
    /// <returns>A new error log message</returns>
    public static LogMessage<T> Error<T>(
        string template,
        T properties,
        EventId eventId = default,
        Exception? exception = null)
    {
        return new LogMessage<T>(LogLevel.Error, template, properties, eventId, exception);
    }
}