namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Thrown when a request is rejected because the circuit is open.
/// </summary>
public sealed class CircuitBrokenException(string circuitKey, TimeSpan breakDuration)
    : Exception($"Circuit '{circuitKey}' is open. Break duration: {breakDuration.TotalSeconds:F1}s")
{
    /// <summary>The key identifying the circuit.</summary>
    public string CircuitKey { get; } = circuitKey;

    /// <summary>The configured break duration.</summary>
    public TimeSpan BreakDuration { get; } = breakDuration;
}
