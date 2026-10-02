using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pragmatic.Jobs.Samples.Samples;

/// <summary>
///     Cancels a pending job via <see cref="IJobScheduler.CancelAsync"/> before
///     the processor reaches it. The job is scheduled with a delay so there is a
///     window to cancel; afterwards the store reports <see cref="JobStatus.Cancelled"/>
///     and the processor never executes it.
/// </summary>
public static class CancellationSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Cancel a pending job ---");

        using var host = JobsHostBuilder.Build();
        await host.StartAsync();

        using var scope = host.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
        var store = host.Services.GetRequiredService<IJobStore>();

        // Schedule far enough out that it is still Pending when we cancel.
        var jobId = await scheduler.ScheduleAsync<SendInvoiceEmailJob>(
            delay: TimeSpan.FromSeconds(30),
            correlationId: "cancel-demo");
        Console.WriteLine($"  enqueued delayed job    : {jobId}");

        var before = await store.GetAsync(jobId);
        Console.WriteLine($"  status before cancel    : {before?.Status}");

        await scheduler.CancelAsync(jobId);
        Console.WriteLine("  CancelAsync called");

        var after = await store.GetAsync(jobId);
        Console.WriteLine($"  status after cancel     : {after?.Status}");
        Console.WriteLine($"  cancelled successfully  : {after?.Status == JobStatus.Cancelled}");

        await host.StopAsync();
        Console.WriteLine();
    }
}
