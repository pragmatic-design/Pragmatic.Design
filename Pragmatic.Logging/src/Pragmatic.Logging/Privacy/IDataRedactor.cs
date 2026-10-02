using Pragmatic.Logging.Attributes;

namespace Pragmatic.Logging.Privacy;

/// <summary>
/// Interface for redacting sensitive data from log entries.
/// </summary>
public interface IDataRedactor
{
    /// <summary>
    /// Redacts sensitive values from a collection of properties.
    /// </summary>
    /// <param name="properties">The properties to redact</param>
    /// <returns>A new dictionary with redacted values</returns>
    Dictionary<string, object?> RedactProperties(Dictionary<string, object?> properties);

    /// <summary>
    /// Redacts sensitive values from a collection of properties with audit context.
    /// </summary>
    /// <param name="properties">The properties to redact</param>
    /// <param name="logLevel">The log level for audit purposes</param>
    /// <param name="categoryName">The category name for audit purposes</param>
    /// <param name="userId">The user ID if available</param>
    /// <param name="correlationId">The correlation ID if available</param>
    /// <returns>A new dictionary with redacted values</returns>
    Dictionary<string, object?> RedactProperties(
        Dictionary<string, object?> properties,
        Microsoft.Extensions.Logging.LogLevel logLevel,
        string categoryName,
        string? userId = null,
        string? correlationId = null);

    /// <summary>
    /// Redacts sensitive content from a message string.
    /// </summary>
    /// <param name="message">The message to redact</param>
    /// <returns>The message with sensitive content redacted</returns>
    string RedactMessage(string message);

    /// <summary>
    /// Determines if a property should be redacted based on its name and characteristics.
    /// </summary>
    /// <param name="propertyName">The name of the property</param>
    /// <param name="characteristics">The property characteristics</param>
    /// <returns>True if the property should be redacted</returns>
    bool ShouldRedact(string propertyName, PropertyCharacteristics characteristics);
}