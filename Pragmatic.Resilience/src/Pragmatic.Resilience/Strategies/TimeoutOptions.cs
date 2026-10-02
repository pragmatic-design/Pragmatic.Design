namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Configuration for the timeout resilience strategy.
/// </summary>
public sealed class TimeoutOptions
{
    /// <summary>Timeout duration (default: 30s).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Timeout enforcement type (default: Optimistic).</summary>
    public TimeoutType TimeoutType { get; set; } = TimeoutType.Optimistic;
}
