using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Jobs.Extensions;
using Pragmatic.Jobs.Services;
using Pragmatic.Jobs.Stores;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Jobs.Tests.Unit;

/// <summary>
///     A host can schedule jobs without running them (#632).
/// </summary>
/// <remarks>
///     <para>
///         The composition registers the processor and the recurring scheduler in every host that sees a module declaring a job, and
///         nothing could leave them out: <c>WithWorkerCount(0)</c> is refused. A web host meant to enqueue while a worker host runs the
///         jobs ran them too, unless it removed the two hosted services by hand.
///     </para>
///     <para>
///         The registrations are made in the order the generated host makes them: the module defaults with the processing services
///         first, then the application's <c>UseJobs</c> callback. A standalone host makes them the other way round, and that order is
///         measured too.
///     </para>
/// </remarks>
public class AHostThatOnlyEnqueuesRunsNothingTests
{
    [Fact]
    public async Task AnEnqueueOnlyHost_StoresTheJob_AndRegistersNothingThatRunsIt()
    {
        var registry = new RecordingRegistry();
        var services = WithRegistry(registry);
        services.AddPragmaticJobs();
        services.AddJobProcessingServices();
        services.AddPragmaticJobs(jobs => jobs.EnqueueOnly());

        await using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().Should().BeEmpty(
            "the processor and the recurring scheduler are what runs jobs, and this host only enqueues them");

        var id = await ScheduleAsync(provider);

        var stored = await provider.GetRequiredService<IJobStore>().GetAsync(id);
        stored.Should().NotBeNull("the host still enqueues: IJobScheduler stays");
        stored!.Status.Should().Be(JobStatus.Pending);
        registry.Ran.Should().Be(0);
    }

    /// <summary>The order a standalone host writes: the mode first, the processing services after.</summary>
    [Fact]
    public async Task AnEnqueueOnlyHost_RegistersNothingThatRunsJobs_WhenTheProcessingServicesComeAfter()
    {
        var services = WithRegistry(new RecordingRegistry());
        services.AddPragmaticJobs(jobs => jobs.EnqueueOnly());
        services.AddJobProcessingServices();

        await using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().Should().BeEmpty();
    }

    /// <summary>The control: by default the same registrations run the job they enqueue.</summary>
    [Fact]
    public async Task ADefaultHost_RunsTheJobItEnqueues()
    {
        var registry = new RecordingRegistry();
        var services = WithRegistry(registry);
        services.AddPragmaticJobs();
        services.AddJobProcessingServices();
        services.AddPragmaticJobs(_ => { });

        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();
        hosted.Should().Contain(service => service is JobProcessorService);
        hosted.Should().Contain(service => service is RecurringJobSchedulerService);

        await ScheduleAsync(provider);

        var processor = hosted.OfType<JobProcessorService>().Single();
        await processor.StartAsync(CancellationToken.None);
        try
        {
            await registry.FirstRun.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }

        registry.Ran.Should().Be(1);
    }

    private static ServiceCollection WithRegistry(RecordingRegistry registry)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IJobTypeRegistry>(registry);
        return services;
    }

    private static async Task<Guid> ScheduleAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IJobScheduler>().ScheduleAsync<NightlyReportJob>().ConfigureAwait(false);
    }

    private sealed class NightlyReportJob : IJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Stands in for a generated registry: knows every job, and counts the bodies it runs.</summary>
    private sealed class RecordingRegistry : IJobTypeRegistry
    {
        private readonly TaskCompletionSource _firstRun = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _ran;

        public int Ran => Volatile.Read(ref _ran);

        public Task FirstRun => _firstRun.Task;

        public Task ExecuteAsync(string jobTypeFqn, string? parametersJson, JobContext context,
            IServiceProvider serviceProvider, CancellationToken ct)
        {
            Interlocked.Increment(ref _ran);
            _firstRun.TrySetResult();
            return Task.CompletedTask;
        }

        public bool Knows(string jobTypeFqn) => true;
        public object? DeserializeParameters(string jobTypeFqn, string? json) => null;
        public string? SerializeParameters(string jobTypeFqn, object? parameters) => null;
        public JobRetryPolicy? GetRetryPolicy(string jobTypeFqn) => null;
        public string? GetContinuationJobType(string jobTypeFqn) => null;
        public int GetPriority(string jobTypeFqn) => 0;
        public int GetMaxConcurrency(string jobTypeFqn) => 0;
    }
}
