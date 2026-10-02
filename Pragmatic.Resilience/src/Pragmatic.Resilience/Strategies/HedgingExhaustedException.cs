namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Thrown when all hedging attempts fail without producing a successful result.
/// </summary>
public sealed class HedgingExhaustedException : Exception
{
    public HedgingExhaustedException(int maxAttempts)
        : this(maxAttempts, null)
    {
    }

    /// <summary>
    ///     Carries the last underlying failure as <see cref="Exception.InnerException"/> so an outer
    ///     strategy filtering by exception type (e.g. a circuit breaker or fallback
    ///     <c>ShouldHandle</c>) can inspect what actually went wrong, rather than only seeing this wrapper.
    /// </summary>
    public HedgingExhaustedException(int maxAttempts, Exception? innerException)
        : base($"All {maxAttempts} hedging attempts failed", innerException)
    {
        MaxAttempts = maxAttempts;
    }

    /// <summary>The maximum number of parallel attempts that were made.</summary>
    public int MaxAttempts { get; }
}
