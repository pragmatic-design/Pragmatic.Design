namespace Pragmatic.Telemetry.Conventions;

/// <summary>
///     Tag names for Pragmatic.Jobs operations.
/// </summary>
public static class JobTags
{
    /// <summary>The job type full name.</summary>
    public const string JobType = "pragmatic.job.type";

    /// <summary>The job instance ID.</summary>
    public const string JobId = "pragmatic.job.id";

    /// <summary>Retry attempt number (0-based).</summary>
    public const string RetryAttempt = "pragmatic.job.retry_attempt";
}
