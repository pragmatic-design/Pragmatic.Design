using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pragmatic.Jobs.Samples.Samples;

/// <summary>
///     Demonstrates delay-based scheduling. One job runs immediately, a second
///     one is held back for a few seconds — the processor respects
///     ScheduledFor and will not pick it up until the clock has caught up.
/// </summary>
public static class DelayedSchedulingSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Delayed scheduling ---");

        using var host = JobsHostBuilder.Build();
        await host.StartAsync();

        using var scope = host.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
        var store = host.Services.GetRequiredService<IJobStore>();

        var immediateId = await scheduler.ScheduleAsync<SendInvoiceEmailJob>(
            correlationId: "immediate");
        var delayedId = await scheduler.ScheduleAsync<SendInvoiceEmailJob>(
            delay: TimeSpan.FromSeconds(4),
            correlationId: "delayed");

        Console.WriteLine($"  immediate job           : {immediateId}");
        Console.WriteLine($"  delayed  job (+4s)      : {delayedId}");

        // 6s is enough for the 2s startup + 1s polling to pick the immediate
        // one, wait the 4s delay, then drain the delayed one as well.
        var delayedResult = await ScheduleAndAwaitSample.WaitForTerminal(store, delayedId, TimeSpan.FromSeconds(20));
        var immediateResult = await store.GetAsync(immediateId);

        Console.WriteLine($"  immediate status        : {immediateResult?.Status.ToString() ?? "missing"}");
        Console.WriteLine($"  delayed   status        : {delayedResult?.Status.ToString() ?? "timeout"}");

        await host.StopAsync();
        Console.WriteLine();
    }
}
