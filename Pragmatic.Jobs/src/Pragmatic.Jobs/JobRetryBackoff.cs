namespace Pragmatic.Jobs;

/// <summary>
///     Backoff applied to <see cref="JobInstance.ScheduledFor"/> when a failed job returns to the
///     queue, so a retry is not re-selected on the very next poll.
/// </summary>
/// <remarks>
///     Shared by every <see cref="IJobStore"/> implementation, so a job retries at the same cadence
///     whichever store is configured.
/// </remarks>
public static class JobRetryBackoff
{
    /// <summary>Upper bound, so a persistently failing job settles at a sane cadence.</summary>
    public static readonly TimeSpan Max = TimeSpan.FromMinutes(30);

    /// <summary>Exponential backoff: 2^attempt seconds, capped at <see cref="Max"/>.</summary>
    public static TimeSpan Compute(int attempt)
    {
        // Clamp the exponent before shifting to avoid overflow on a long-failing job.
        var exponent = Math.Min(Math.Max(attempt, 1), 16);
        var seconds = Math.Min(Math.Pow(2, exponent), Max.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}
