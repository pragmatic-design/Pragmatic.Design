namespace Pragmatic.Resilience.State;

/// <summary>
/// Stores circuit breaker state. Default: in-memory singleton.
/// Provider: Redis, DB for distributed scenarios.
/// </summary>
public interface ICircuitBreakerStateStore
{
    /// <summary>Get current circuit state and metadata.</summary>
    Task<CircuitSnapshot> GetSnapshotAsync(string circuitKey, CancellationToken ct = default);

    /// <summary>Record a successful execution.</summary>
    Task RecordSuccessAsync(string circuitKey, CancellationToken ct = default);

    /// <summary>Record a failed execution.</summary>
    Task RecordFailureAsync(string circuitKey, CancellationToken ct = default);

    /// <summary>Transition the circuit to a new state.</summary>
    Task TransitionToAsync(string circuitKey, CircuitState newState, TimeSpan? breakDuration = null, CancellationToken ct = default);

    /// <summary>Atomically transition from Open to HalfOpen if the cooldown has elapsed. Returns true if this caller won the race.</summary>
    Task<bool> TryTransitionToHalfOpenAsync(string circuitKey, DateTimeOffset now, CancellationToken ct = default);
}
