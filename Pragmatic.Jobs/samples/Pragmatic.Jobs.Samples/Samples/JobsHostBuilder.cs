using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.Extensions;

namespace Pragmatic.Jobs.Samples.Samples;

/// <summary>
///     Shared host bootstrap for standalone scenarios. Wires the SG-generated
///     job registrations alongside the runtime services so the scheduler and
///     the processor background service can run without Composition.
/// </summary>
internal static class JobsHostBuilder
{
    public static IHost Build(Action<JobsBuilder>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddPragmaticJobs(jobs =>
        {
            jobs.WithWorkerCount(2);
            jobs.WithPollingInterval(1);
            jobs.WithBatchSize(10);
            configure?.Invoke(jobs);
        });

        // SG-generated: registers every [Job]/[RecurringJob] class, swaps in the generated job
        // type registry and exposes the declared recurring definitions.
        builder.Services.AddDiscoveredJobs();

        builder.Services.AddJobProcessingServices();

        return builder.Build();
    }
}
