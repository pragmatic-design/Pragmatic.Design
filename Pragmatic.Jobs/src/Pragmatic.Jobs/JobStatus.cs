namespace Pragmatic.Jobs;

/// <summary>
///     Lifecycle status of a job instance.
/// </summary>
public enum JobStatus
{
    /// <summary>Job is enqueued and waiting to be processed.</summary>
    Pending = 0,

    /// <summary>Job is currently being executed by a worker.</summary>
    Running = 1,

    /// <summary>Job completed successfully.</summary>
    Completed = 2,

    /// <summary>Job failed after exhausting all retry attempts.</summary>
    Failed = 3,

    /// <summary>Job was cancelled before execution.</summary>
    Cancelled = 4
}
