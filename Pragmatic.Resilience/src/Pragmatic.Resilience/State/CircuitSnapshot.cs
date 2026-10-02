namespace Pragmatic.Resilience.State;

/// <summary>
/// Snapshot of circuit breaker state at a point in time.
/// </summary>
public sealed record CircuitSnapshot
{
    public required CircuitState State { get; init; }
    public int FailureCount { get; init; }
    public int SuccessCount { get; init; }
    public DateTimeOffset? LastFailureTime { get; init; }
    public DateTimeOffset? OpenedAt { get; init; }
    public TimeSpan? BreakDuration { get; init; }

    /// <summary>
    /// The timestamp at which this snapshot was taken.
    /// Always populated by the state store (via TimeProvider) — do not rely on the default value.
    /// </summary>
    public DateTimeOffset Now { get; init; }

    /// <summary>Whether the break duration has elapsed and circuit can probe.</summary>
    public bool CanProbe => State == CircuitState.Open &&
                            OpenedAt.HasValue &&
                            BreakDuration.HasValue &&
                            Now >= OpenedAt.Value + BreakDuration.Value;
}
