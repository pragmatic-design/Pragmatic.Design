namespace Pragmatic.Logging.Attributes;

/// <summary>
/// Behavioral characteristics for log properties, used by the redaction pipeline
/// (<see cref="Pragmatic.Logging.Privacy.IDataRedactor"/>) to decide how a property is handled.
/// </summary>
[Flags]
public enum PropertyCharacteristics
{
    /// <summary>
    /// No special characteristics.
    /// </summary>
    None = 0,

    /// <summary>
    /// Property contains sensitive data and should be redacted.
    /// </summary>
    Redacted = 1,

    /// <summary>
    /// Property is used for correlation across log entries.
    /// </summary>
    Correlation = 2,

    /// <summary>
    /// Property contains business-critical information.
    /// </summary>
    Business = 4,

    /// <summary>
    /// Property is related to performance metrics.
    /// </summary>
    Performance = 8,

    /// <summary>
    /// Property contains security-related information.
    /// </summary>
    Security = 16
}
