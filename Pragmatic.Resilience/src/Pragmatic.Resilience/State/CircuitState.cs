namespace Pragmatic.Resilience.State;

/// <summary>
/// Represents the state of a circuit breaker.
/// </summary>
public enum CircuitState
{
    /// <summary>Normal operation — requests flow through.</summary>
    Closed,

    /// <summary>Circuit is open — requests are rejected immediately.</summary>
    Open,

    /// <summary>Probing — a single request is allowed to test if the service recovered.</summary>
    HalfOpen
}
