using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pragmatic.Jobs.Samples.Samples;

/// <summary>
///     Schedules a single parameterless job and waits for the processor to
///     drain it. Demonstrates the minimum contract: SG-generated type
///     registry + JobProcessorService + IJobScheduler all talking to an
///     in-memory IJobStore.
/// </summary>
public static class ScheduleAndAwaitSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- ScheduleAsync + JobProcessorService drain ---");

        using var host = JobsHostBuilder.Build();
        await host.StartAsync();

        using var scope = host.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
        var store = host.Services.GetRequiredService<IJobStore>();

        var jobId = await scheduler.ScheduleAsync<SendInvoiceEmailJob>(
            correlationId: "demo-corr-1");
        Console.WriteLine($"  enqueued job            : {jobId}");

        var completedJob = await WaitForTerminal(store, jobId, TimeSpan.FromSeconds(15));

        Console.WriteLine($"  terminal status         : {completedJob?.Status.ToString() ?? "timeout"}");
        Console.WriteLine($"  attempts recorded       : {completedJob?.Attempt ?? -1}");

        await host.StopAsync();
        Console.WriteLine();
    }

    internal static async Task<JobInstance?> WaitForTerminal(
        IJobStore store,
        Guid jobId,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var job = await store.GetAsync(jobId);
            if (job is not null &&
                job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                return job;

            await Task.Delay(200);
        }

        return await store.GetAsync(jobId);
    }
}
