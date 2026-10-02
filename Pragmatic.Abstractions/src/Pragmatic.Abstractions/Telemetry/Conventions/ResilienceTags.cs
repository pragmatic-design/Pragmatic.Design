namespace Pragmatic.Telemetry.Conventions;

/// <summary>
///     Tag names for resilience operations (retry, circuit breaker, bulkhead, timeout).
/// </summary>
public static class ResilienceTags
{
    /// <summary>The resilience policy name.</summary>
    public const string Policy = "pragmatic.resilience.policy";

    /// <summary>The current retry attempt number.</summary>
    public const string Attempt = "pragmatic.resilience.attempt";

    /// <summary>The resilience outcome: "success", "retry", "rejected", "timeout", "circuit_open".</summary>
    public const string Outcome = "pragmatic.resilience.outcome";
}
