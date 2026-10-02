namespace Pragmatic.Resilience;

/// <summary>
/// Context passed through the resilience pipeline. Carries metadata for strategies.
/// </summary>
public sealed class ResilienceContext
{
    /// <summary>Logical operation name (e.g., "PlaceOrder", "GetUser").</summary>
    public required string OperationName { get; init; }

    /// <summary>Optional operation key for circuit breaker state isolation.</summary>
    public string? OperationKey { get; init; }

    /// <summary>Current retry attempt (0 = first attempt).</summary>
    public int AttemptNumber { get; internal set; }

    /// <summary>Total elapsed time since the first attempt.</summary>
    public TimeSpan TotalElapsed { get; internal set; }

    /// <summary>Arbitrary properties for cross-strategy communication. Lazily initialized to avoid allocation when unused.</summary>
    private IDictionary<string, object>? _properties;

    public IDictionary<string, object> Properties
    {
        get
        {
            // LazyInitializer.EnsureInitialized is thread-safe; avoids double-allocation race.
            return System.Threading.LazyInitializer.EnsureInitialized(
                ref _properties,
                static () => new Dictionary<string, object>());
        }
    }

    /// <summary>
    ///     Creates an isolated copy for a single parallel attempt (e.g. hedging), carrying the same
    ///     identity but a fresh <see cref="AttemptNumber"/> and <see cref="Properties"/>. Without this,
    ///     concurrent hedged attempts would race on the shared mutable <see cref="AttemptNumber"/>.
    /// </summary>
    internal ResilienceContext CloneForAttempt() => new()
    {
        OperationName = OperationName,
        OperationKey = OperationKey
    };
}
