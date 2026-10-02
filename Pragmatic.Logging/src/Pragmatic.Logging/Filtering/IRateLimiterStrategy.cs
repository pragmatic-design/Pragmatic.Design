namespace Pragmatic.Logging.Filtering;

/// <summary>
/// Common contract for the rate-limiting strategies used by the logging filters.
/// Implementations must be thread-safe.
/// </summary>
internal interface IRateLimiterStrategy
{
    /// <summary>
    /// Returns <c>true</c> if a message is allowed under the limiter at the current instant,
    /// consuming the corresponding budget; otherwise <c>false</c>.
    /// </summary>
    bool ShouldAllow();
}
