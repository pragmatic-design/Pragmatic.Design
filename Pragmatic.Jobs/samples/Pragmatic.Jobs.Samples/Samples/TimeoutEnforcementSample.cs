using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pragmatic.Jobs.Samples.Samples;

/// <summary>
///     Demonstrates <c>[Timeout]</c> enforcement at runtime. <see cref="TimeoutDemoJob"/>
///     declares <c>[Timeout(TimeoutSeconds = 1)]</c> but attempts ~10s of work; the
///     SG-generated invoker cancels the job's token after 1 second, so the job is
///     interrupted long before it would otherwise finish.
///     <para>
///         Note: a job cancelled by its timeout throws <c>OperationCanceledException</c>,
///         which the processor does NOT treat as a retriable failure (it filters
///         OCE). The job's lease is left to expire and is then re-queued by
///         <c>ReleaseExpiredLeasesAsync</c>. This sample observes the timeout via the
///         job's own flag so the demo stays bounded and deterministic.
///     </para>
/// </summary>
public static class TimeoutEnforcementSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- [Timeout] enforcement ---");

        using var host = JobsHostBuilder.Build();
        await host.StartAsync();

        using var scope = host.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();

        var jobId = await scheduler.ScheduleAsync<TimeoutDemoJob>(correlationId: "timeout-demo");
        Console.WriteLine($"  enqueued TimeoutDemoJob : {jobId}");
        Console.WriteLine("  job wants ~10s of work, [Timeout]=1s");

        // Poll the job's own flag. With a 1s timeout the work is interrupted well
        // inside this 8s budget; we never wait for the full 10s the job requested.
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(8);
        while (!TimeoutDemoJob.TimedOut && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(200);

        Console.WriteLine($"  timeout fired           : {TimeoutDemoJob.TimedOut}");
        Console.WriteLine($"  loops completed         : {TimeoutDemoJob.LoopsCompleted} (would be 20 without timeout)");

        await host.StopAsync();
        Console.WriteLine();
    }
}
