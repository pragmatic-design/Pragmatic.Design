namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Options for the circuit breaker strategy.
/// </summary>
public sealed class CircuitBreakerOptions
{
    /// <summary>Number of consecutive failures before opening the circuit.</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>How long the circuit stays open before allowing a probe request.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Optional predicate to determine if an exception should count as a failure.</summary>
    public Func<Exception, bool>? ShouldHandle { get; set; }
}
