using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Pragmatic.Resilience.Attributes;

namespace Pragmatic.Jobs.Samples;

/// <summary>
///     Example: recurring job that runs daily at 2 AM UTC.
///     The SG generates DailyReportJob_Invoker with retry + telemetry.
/// </summary>
[RecurringJob("0 2 * * *", Id = "daily-report")]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter, BaseDelayMs = 1000)]
[Timeout(TimeoutSeconds = 600)]
public sealed partial class DailyReportJob : IJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        Console.WriteLine($"[{context.ScheduledAt:yyyy-MM-dd}] Generating daily report (attempt {context.Attempt})...");
        return Task.CompletedTask;
    }
}
