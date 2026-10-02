using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Pragmatic.Resilience.Attributes;

namespace Pragmatic.Jobs.Samples;

/// <summary>
///     Test fixture: fails on the first two executions (tracked in a static counter)
///     and succeeds on the third. The JobProcessorService honours <c>[Retry]</c>
///     (3 attempts total here, fixed 500ms backoff for a tight demo) so the sample
///     can observe the runtime re-enqueueing the job instance until it settles.
/// </summary>
[Job]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.Fixed, BaseDelayMs = 500)]
public sealed partial class FlakyEmailJob : IJob
{
    /// <summary>Count of executions across the whole process, shared by all retries.</summary>
    public static int Executions;

    public Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var n = Interlocked.Increment(ref Executions);
        if (n < 3)
            throw new InvalidOperationException($"transient failure (execution #{n})");

        Console.WriteLine($"  FlakyEmailJob succeeded on execution #{n}");
        return Task.CompletedTask;
    }
}
