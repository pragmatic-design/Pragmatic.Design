using Pragmatic.Resilience.Attributes;

namespace Pragmatic.Jobs;

/// <summary>
///     The retry configuration a job declared with <see cref="RetryAttribute"/>, resolved from the
///     source generator at runtime.
/// </summary>
/// <remarks>
///     Retries are applied by the store, not inside the invoker. An in-process retry loop is lost
///     when the worker crashes, never advances the persisted <c>Attempt</c>, and is invisible to
///     anything reading the job table — so <c>[Retry(MaxAttempts = 5)]</c> and the attempt count an
///     operator saw were two unrelated numbers.
/// </remarks>
/// <param name="MaxAttempts">Total executions allowed, including the first.</param>
/// <param name="BaseDelay">Base delay used by <paramref name="Strategy"/>.</param>
/// <param name="Strategy">How the delay grows between attempts.</param>
public sealed record JobRetryPolicy(int MaxAttempts, TimeSpan BaseDelay, BackoffStrategy Strategy)
{
    /// <summary>Computes the delay before the given (1-based) attempt is retried.</summary>
    public TimeSpan ComputeDelay(int attempt)
    {
        // The first retry waits the base delay, so the exponent counts from zero: `attempt` is 1-based
        // and base * 2^0 is the base. Counting it from one started a doubling ahead of ordinary
        // exponential backoff, and ahead of the message engine reading the same declaration.
        //
        // Clamped before shifting, to avoid overflow on a long-failing job.
        var exponent = Math.Min(Math.Max(attempt - 1, 0), 16);

        var delay = Strategy switch
        {
            BackoffStrategy.Fixed => BaseDelay,
            // Unwritten on the declaration means this engine decides, and this engine's answer is
            // plain exponential. It shares the branch with Exponential rather than falling through to
            // the catch-all, where "unset" would silently have become "with jitter".
            BackoffStrategy.Unspecified or BackoffStrategy.Exponential => BaseDelay * Math.Pow(2, exponent),
            // Jitter spreads a fleet's retries so a shared dependency coming back online is not hit
            // by every failed job at the same instant.
            _ => BaseDelay * Math.Pow(2, exponent)
                 + TimeSpan.FromMilliseconds(
                     System.Security.Cryptography.RandomNumberGenerator.GetInt32(
                         0, Math.Max((int)BaseDelay.TotalMilliseconds, 1)))
        };

        return delay > JobRetryBackoff.Max ? JobRetryBackoff.Max : delay;
    }
}
