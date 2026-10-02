namespace Pragmatic.Gateway.Resilience;

/// <summary>
///     Resilience policy for a single YARP cluster (backend).
/// </summary>
public sealed class ClusterResiliencePolicy
{
    /// <summary>Request timeout. Default: 10s. Null = no timeout.</summary>
    public TimeSpan? Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Enable circuit breaker. Default: true.</summary>
    public bool CircuitBreakerEnabled { get; set; } = true;

    /// <summary>Consecutive failures before opening the circuit. Default: 5.</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>How long the circuit stays open before allowing a probe. Default: 30s.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     HTTP status codes that count as failure — lower bound (inclusive). Default: 500.
    ///     Must be &lt;= <see cref="FailureStatusCodeMax" />.
    /// </summary>
    public int FailureStatusCodeMin { get; set; } = 500;

    /// <summary>
    ///     HTTP status codes that count as failure — upper bound (inclusive). Default: 599.
    ///     Must be &gt;= <see cref="FailureStatusCodeMin" />.
    /// </summary>
    public int FailureStatusCodeMax { get; set; } = 599;

    /// <summary>
    ///     Validates that <see cref="FailureStatusCodeMin" /> &lt;= <see cref="FailureStatusCodeMax" />.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the range is inverted.</exception>
    public void Validate()
    {
        if (FailureStatusCodeMin > FailureStatusCodeMax)
            throw new InvalidOperationException(
                $"Invalid resilience policy: FailureStatusCodeMin ({FailureStatusCodeMin}) must be <= FailureStatusCodeMax ({FailureStatusCodeMax}).");
    }
}
