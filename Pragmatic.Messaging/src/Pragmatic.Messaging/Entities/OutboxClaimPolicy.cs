namespace Pragmatic.Messaging.Entities;

/// <summary>
///     The claim and retry rules every <see cref="IOutboxSource" /> applies to its rows, whatever store holds them: how long a claim holds, the
///     token that marks the rows one poll won, and how long a failed message waits before it is eligible again.
/// </summary>
public static class OutboxClaimPolicy
{
    /// <summary>How long a claim is held before another worker may re-grab the row (crash recovery).</summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);

    // Backoff is capped so a long-failing message still gets retried at a sane cadence
    // (and never overflows when shifting by a large RetryCount).
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);

    /// <summary>
    ///     A unique per-poll token: a source stamps the eligible rows with it, then selects only what it won. This makes claim+select atomic
    ///     across replicas — a plain <c>Where(...).Take()</c> lets every replica grab the same rows and deliver each N times.
    /// </summary>
    public static string NewClaimToken() => $"{Environment.MachineName}:{Guid.NewGuid():N}";

    /// <summary>Exponential backoff: 2^attempt seconds, capped at 30 minutes.</summary>
    public static TimeSpan Backoff(int attempt)
    {
        // Cap the exponent before shifting to avoid overflow on a long-failing message.
        var exponent = Math.Min(attempt, 16);
        var seconds = Math.Min(Math.Pow(2, exponent), MaxBackoff.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}
