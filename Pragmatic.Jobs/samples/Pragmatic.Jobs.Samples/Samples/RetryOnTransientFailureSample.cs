using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pragmatic.Jobs.Samples.Samples;

/// <summary>
///     Schedules <see cref="FlakyEmailJob"/> — a job wired with
///     <c>[Retry(MaxAttempts=3)]</c> that throws on attempts 1 and 2 and succeeds
///     on attempt 3. Demonstrates the retry runtime end-to-end: the processor
///     catches the failure, bumps the attempt counter, honours the configured
///     backoff, and re-enqueues until the job terminates (Completed or Failed).
///
///     Recurring cron + timeout enforcement are exercised by the job definitions
///     shipped alongside this sample (<see cref="DailyReportJob"/>,
///     <see cref="RecalculatePricesJob"/>, etc.) but they are driven by a
///     background scheduler on long cron cadences — out of scope for a
///     deterministic console demo.
/// </summary>
public static class RetryOnTransientFailureSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Retry on transient failure ---");
        FlakyEmailJob.Executions = 0;

        using var host = JobsHostBuilder.Build();
        await host.StartAsync();

        using var scope = host.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
        var store = host.Services.GetRequiredService<IJobStore>();

        var jobId = await scheduler.ScheduleAsync<FlakyEmailJob>(correlationId: "retry-demo");
        Console.WriteLine($"  enqueued FlakyEmailJob  : {jobId}");

        // 500ms backoff × 2 retries = ~1s extra on top of worker polling. Give it
        // a generous 15s deadline so a slower CI environment doesn't flake.
        var terminal = await ScheduleAndAwaitSample.WaitForTerminal(store, jobId, TimeSpan.FromSeconds(15));

        Console.WriteLine($"  terminal status         : {terminal?.Status.ToString() ?? "timeout"}");
        Console.WriteLine($"  attempts recorded       : {terminal?.Attempt ?? -1} (retry ran 2 extra attempts)");
        Console.WriteLine($"  total executions        : {FlakyEmailJob.Executions}");

        await host.StopAsync();
        Console.WriteLine();
    }
}
