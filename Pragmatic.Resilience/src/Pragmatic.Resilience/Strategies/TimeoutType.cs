namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Timeout enforcement approach.
/// </summary>
public enum TimeoutType
{
    /// <summary>
    /// Relies on CancellationToken cooperation. Preferred for operations that honor cancellation.
    /// </summary>
    Optimistic,

    /// <summary>
    /// Races Task.Delay against the operation. For operations that don't honor cancellation.
    /// The operation may continue running in the background.
    /// </summary>
    Pessimistic
}
