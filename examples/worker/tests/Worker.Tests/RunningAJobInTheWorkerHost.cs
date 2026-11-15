using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Jobs;
using Pragmatic.Testing.Hosting;
using Worker.Host.Infrastructure.Jobs;
using Worker.Host.Infrastructure.Services;
using Worker.Host;
using Xunit;

namespace Worker.Tests;

/// <summary>
///     The worker host, built from its entry point (<c>PragmaticApp.RunWorkerAsync</c>) by <see cref="PragmaticWorkerFactory{TEntryPoint}" />:
///     a job scheduled through it is run by the host's own job processor (#749).
/// </summary>
public sealed class RunningAJobInTheWorkerHost
{
    [Fact]
    public async Task A_scheduled_job_is_run_by_the_hosts_processor_and_reaches_the_overridden_sender()
    {
        var sender = new RecordingDigestSender();
        await using var worker = new PragmaticWorkerFactory<WorkerHostModule>()
            .ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<IDigestSender>(sender)))
            .UseEnvironment("Testing");
        await worker.StartAsync();

        Guid scheduled;
        await using (var scope = worker.Services.CreateAsyncScope())
            scheduled = await scope.ServiceProvider.GetRequiredService<IJobScheduler>().ScheduleAsync<SendDigestJob>();

        // Completed by the job itself, run by the processor the host started: no polling of the store.
        Assert.Equal(scheduled, await sender.Sent.WaitAsync(TimeSpan.FromSeconds(30)));
        await worker.StopAsync();
    }

    /// <summary>
    ///     The control: without an override the host is the application's own, with the worker's sender and the environment the test asked
    ///     for.
    /// </summary>
    [Fact]
    public async Task Without_an_override_the_host_has_the_applications_registrations()
    {
        await using var worker = new PragmaticWorkerFactory<WorkerHostModule>().UseEnvironment("Testing");

        Assert.IsType<LoggedDigestSender>(worker.Services.GetRequiredService<IDigestSender>());
        Assert.Equal("Testing", worker.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>().EnvironmentName);
    }

    /// <summary>An override given once the host is built would change nothing: the factory refuses it instead of ignoring it.</summary>
    [Fact]
    public async Task An_override_after_the_host_is_built_is_refused()
    {
        await using var worker = new PragmaticWorkerFactory<WorkerHostModule>();
        _ = worker.Services;

        Assert.Throws<InvalidOperationException>(() => worker.ConfigureServices(_ => { }));
    }
}
