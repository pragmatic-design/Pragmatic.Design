namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Specifies a timeout for a saga step. If the step doesn't complete
///     within the specified duration, the saga transitions to a timeout state.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SagaTimeoutAttribute : Attribute
{
    /// <summary>Timeout duration as TimeSpan string (e.g., "00:30:00" for 30 minutes).</summary>
    public required string Duration { get; set; }
}
