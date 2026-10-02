using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging;

/// <summary>
/// Represents a structured log message with compile-time type safety and zero-allocation capabilities.
/// This interface defines the contract for type-safe log messages that support high-performance scenarios.
/// </summary>
/// <typeparam name="T">The type containing the log message properties</typeparam>
/// <remarks>
/// <para>
/// This interface is designed to support both traditional object-based logging and high-performance
/// zero-allocation scenarios using struct implementations.
/// </para>
/// <para>
/// The generic type parameter T allows for strongly typed properties while maintaining flexibility
/// for different property container types (anonymous objects, records, structs, etc.).
/// </para>
/// </remarks>
/// <example>
/// Example implementation with anonymous type:
/// <code>
/// ILogMessage&lt;object&gt; message = new LogMessage&lt;object&gt;(
///     LogLevel.Information,
///     "User {UserId} performed {Action}",
///     new { UserId = "john.doe", Action = "login" },
///     new EventId(1001, "UserLogin"));
/// </code>
/// 
/// Example with strongly typed properties:
/// <code>
/// public record UserLoginProperties(string UserId, string IPAddress, DateTime Timestamp);
/// 
/// ILogMessage&lt;UserLoginProperties&gt; typedMessage = new LogMessage&lt;UserLoginProperties&gt;(
///     LogLevel.Information,
///     "User {UserId} logged in from {IPAddress} at {Timestamp}",
///     new UserLoginProperties("john.doe", "192.168.1.1", DateTime.UtcNow));
/// </code>
/// </example>
public interface ILogMessage<out T>
{
    /// <summary>
    /// Gets the log level for this message.
    /// </summary>
    LogLevel LogLevel { get; }

    /// <summary>
    /// Gets the message template with placeholders.
    /// </summary>
    string Template { get; }

    /// <summary>
    /// Gets the structured properties for this log message.
    /// </summary>
    T Properties { get; }

    /// <summary>
    /// Gets the event identifier for this log message.
    /// </summary>
    EventId EventId { get; }

    /// <summary>
    /// Gets the exception associated with this log message, if any.
    /// </summary>
    Exception? Exception { get; }
}