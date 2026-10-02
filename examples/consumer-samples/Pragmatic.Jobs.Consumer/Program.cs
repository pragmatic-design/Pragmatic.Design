using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Extensions;

namespace Pragmatic.Jobs.Consumer;

internal static class Program
{
    private static async Task Main()
    {
        Console.WriteLine("=== Pragmatic.Jobs.Consumer (PackageReference) ===");

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddPragmaticJobs(cfg =>
        {
            cfg.WithWorkerCount(1);
            cfg.WithPollingInterval(1);
        });

        // SG-generated extension: registers [Job] classes as scoped services.
        // The method is emitted into THIS assembly's namespace by the analyzer.
        builder.Services.AddPragmaticJobs();

        // SG-generated type registry (also in this assembly's namespace).
        builder.Services.AddSingleton<IJobTypeRegistry, PragmaticJobTypeRegistry>();
        builder.Services.AddJobProcessingServices();

        using var host = builder.Build();
        await host.StartAsync();

        using var scope = host.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
        var store = host.Services.GetRequiredService<IJobStore>();

        var jobId = await scheduler.ScheduleAsync<GreetingJob>();
        Console.WriteLine($"  scheduled job           : {jobId}");

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        JobInstance? job = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            job = await store.GetAsync(jobId);
            if (job is { Status: JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled })
                break;
            await Task.Delay(200);
        }

        Console.WriteLine($"  terminal status         : {job?.Status.ToString() ?? "timeout"}");

        await host.StopAsync();
        Console.WriteLine("=== done ===");
    }
}
