namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Options for the bulkhead (concurrency limiter) strategy.
/// </summary>
public sealed class BulkheadOptions
{
    /// <summary>Maximum number of concurrent executions.</summary>
    public int MaxConcurrency { get; set; } = 10;

    /// <summary>Maximum number of queued actions when all slots are taken.</summary>
    public int MaxQueuedActions { get; set; } = 0;

    /// <summary>Maximum time to wait in the queue. Zero means no wait.</summary>
    public TimeSpan QueueTimeout { get; set; } = TimeSpan.Zero;
}
