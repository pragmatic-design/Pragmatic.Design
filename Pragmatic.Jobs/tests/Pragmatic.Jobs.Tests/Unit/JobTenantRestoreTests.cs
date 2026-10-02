using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.Services;
using Pragmatic.Jobs.Stores;
using Pragmatic.MultiTenancy;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Testing;
using Xunit;

namespace Pragmatic.Jobs.Tests.Unit;

/// <summary>
///     A job runs under the tenant it was enqueued for, and a job enqueued under none runs under none.
/// </summary>
/// <remarks>
///     <para>
///         The processor declares the tenant with <c>TenantScope.BeginScope</c>, the mechanism the
///         framework's background workers share, rather than resolving <c>IMutableTenantContext</c> from its
///         own scope and calling <c>SetTenant</c> — a gesture that, copied by hand into each worker,
///         drifts.
///     </para>
///     <para>
///         ⚠️ The second case is the one that matters, and it is why the first is not enough. The tenant
///         filter is fail-closed: a worker that forgets to declare its tenant reads zero rows, and zero
///         rows is indistinguishable from an empty table. So "the tenant does not leak from the previous
///         job" cannot be left to reading the code — a sixth worker written tomorrow is right only by
///         imitation.
///     </para>
/// </remarks>
public class JobTenantRestoreTests
{
    private const string JobType = "TestApp.TenantReadingJob";

    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Enqueues one job per tenant, then polls and runs them in order, and reports what each body
    ///     saw.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The processor is driven directly — <c>PollPendingAsync</c> then <c>ProcessJobAsync</c> —
    ///     rather than started as a hosted service. Through <c>StartAsync</c> this case would also assert
    ///     that the polling loop is scheduled promptly, which on a loaded machine is false often enough
    ///     to time out a test that passes alone.
    ///     <para>
    ///         What is under test is where <c>TenantScope</c> is opened, which is
    ///         <c>ProcessJobAsync</c>. Sequencing the two jobs here also states the "even after a
    ///         tenanted one" claim instead of hoping the loop takes them in order.
    ///     </para>
    ///     <para>
    ///         ⚠️ What this does not cover: that the loop reaches a job at all. That is asserted by
    ///         <c>JobMaxConcurrencyTests</c>, which drives the hosted service, and by
    ///         <c>TheProcessorLeavesTheCallersContextTests</c>.
    ///     </para>
    /// </remarks>
    private static async Task<List<string?>> RunAsync(params string?[] tenantIds)
    {
        var observed = new List<string?>();
        var clock = new TestClock(Now);
        var jobStore = new InMemoryJobStore(clock);

        foreach (var tenantId in tenantIds)
        {
            await jobStore.EnqueueAsync(new JobInstance
            {
                Id = Guid.NewGuid(),
                JobType = JobType,
                Status = JobStatus.Pending,
                ScheduledFor = Now.AddMinutes(-1),
                TenantId = tenantId,
                MaxAttempts = 1
            }).ConfigureAwait(true);
        }

        var registry = new TenantRecordingRegistry(observed);

        var services = new ServiceCollection();
        services.AddSingleton<IJobStore>(jobStore);
        services.AddSingleton<IJobTypeRegistry>(registry);
        services.AddSingleton<IClock>(clock);

        // ⚠️ TenantScope itself, not the AmbientTenantContext an application registers. Not a
        // shortcut: this test project does not reference the Pragmatic.MultiTenancy package, and
        // inside a job scope the real
        // composite reduces to exactly this, because it prefers the request tenant and there is no
        // request. That the composite reduces that way is asserted where it lives, in
        // AmbientTenantContextTests.UnresolvedRequest_FallsBackToTenantScope.
        services.AddScoped<ITenantContext>(_ => new TenantScope());

        var provider = services.BuildServiceProvider();
        registry.Tenants = provider;

        var processor = new JobProcessorService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            clock,
            new JobsOptions { WorkerCount = 1, PollingIntervalSeconds = 1, StartupDelaySeconds = 0 },
            NullLogger<JobProcessorService>.Instance);

        // The order the jobs were enqueued in: the second claim is about what the first left behind.
        var pending = await processor.PollPendingAsync(CancellationToken.None).ConfigureAwait(true);
        pending.Count.Should().Be(tenantIds.Length, "every enqueued job is ready to run");

        foreach (var job in pending)
            await processor.ProcessJobAsync(job, CancellationToken.None).ConfigureAwait(true);

        return observed;
    }

    /// <summary>The claim: the job body sees the tenant the job was enqueued for.</summary>
    [Fact]
    public async Task AJobEnqueuedForATenant_RunsUnderThatTenant()
    {
        var observed = await RunAsync("tenant-a").ConfigureAwait(true);

        observed.Should().BeEquivalentTo(["tenant-a"]);
    }

    /// <summary>
    ///     The control, and the one the fail-closed filter makes necessary: a job with no tenant runs
    ///     under none, even straight after one that had a tenant.
    /// </summary>
    /// <remarks>
    ///     Without it, "the job sees its tenant" is satisfied by an ambient that is set once and never
    ///     cleared — which reads correctly for the first job and attributes every later one to it.
    ///     <para>
    ///         ⚠️ Measured, and it says something narrower than it looks: removing the <c>using</c> from
    ///         the processor does <b>not</b> make this fail. A write to an <c>AsyncLocal</c> inside an
    ///         async method does not escape to its caller, so one job cannot carry its tenant into the
    ///         next — the isolation is structural, not a consequence of the dispose. What this case
    ///         actually pins is that an untenanted job reads <c>null</c> and not some earlier value,
    ///         which is the assertion the fail-closed filter makes worth having; the <c>using</c> earns
    ///         its place within a single flow, where a job body opens a scope of its own.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AJobWithNoTenant_RunsUnderNone_EvenAfterATenantedOne()
    {
        var observed = await RunAsync("tenant-a", null).ConfigureAwait(true);

        observed.Should().BeEquivalentTo(["tenant-a", null]);
    }

    /// <summary>
    ///     ⚠️ The round trip: enqueued through the scheduler, run by the processor, same tenant.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The two cases above enqueue by writing <c>JobInstance.TenantId</c> straight into the
    ///         store, so they measure the restore and take the capture on trust. The column has several
    ///         writers on the enqueue path — the recurring scheduler, continuations, and the ad-hoc path
    ///         an application uses — and one that forgets leaves it null.
    ///     </para>
    ///     <para>
    ///         So this one goes through <see cref="JobScheduler" /> inside a scope, which is what a
    ///         request does. Two halves written by two different classes, and this is the only case
    ///         that fails if either forgets.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AJobScheduledInsideATenant_RunsUnderIt()
    {
        var observed = new List<string?>();
        var clock = new TestClock(Now);
        var jobStore = new InMemoryJobStore(clock);
        var registry = new TenantRecordingRegistry(observed);

        var services = new ServiceCollection();
        services.AddSingleton<IJobStore>(jobStore);
        services.AddSingleton<IJobTypeRegistry>(registry);
        services.AddSingleton<IClock>(clock);
        services.AddScoped<ITenantContext>(_ => new TenantScope());

        var provider = services.BuildServiceProvider();
        registry.Tenants = provider;

        var scheduler = new JobScheduler(
            jobStore, clock, new JobsOptions(), NullLogger<JobScheduler>.Instance,
            Pragmatic.Serialization.PragmaticJsonOptions.Default, registry, new TenantScope());

        using (TenantScope.BeginScope("tenant-round-trip"))
            await scheduler.ScheduleAsync<TenantReadingJob>().ConfigureAwait(true);

        var processor = new JobProcessorService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            clock,
            new JobsOptions { WorkerCount = 1, PollingIntervalSeconds = 1, StartupDelaySeconds = 0 },
            NullLogger<JobProcessorService>.Instance);

        var pending = await processor.PollPendingAsync(CancellationToken.None).ConfigureAwait(true);
        pending.Should().ContainSingle("the scheduler enqueued one job");

        foreach (var job in pending)
            await processor.ProcessJobAsync(job, CancellationToken.None).ConfigureAwait(true);

        observed.Should().BeEquivalentTo(["tenant-round-trip"],
            "the tenant the caller was in reached the body through the row, not through a parameter");
    }

    /// <summary>A job that does nothing: the registry above is what observes the tenant.</summary>
    private sealed class TenantReadingJob : IJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Records what <see cref="ITenantContext" /> says while each job body runs.</summary>
    private sealed class TenantRecordingRegistry(List<string?> observed) : IJobTypeRegistry
    {
        private readonly Lock _lock = new();

        public IServiceProvider? Tenants { get; set; }

        public Task ExecuteAsync(string jobTypeFqn, string? parametersJson, JobContext context,
            IServiceProvider serviceProvider, CancellationToken ct)
        {
            var tenant = serviceProvider.GetRequiredService<ITenantContext>();

            lock (_lock)
                observed.Add(tenant.TenantId);

            return Task.CompletedTask;
        }

        // This fake stands in for a generated registry, and a generated one knows the jobs of its
        // assembly. Answering false would make the scheduler refuse what these tests schedule — which
        // is the refusal working, not a failure of theirs.
        public bool Knows(string jobTypeFqn) => true;

        public object? DeserializeParameters(string jobTypeFqn, string? json) => null;
        public string? SerializeParameters(string jobTypeFqn, object? parameters) => null;
        public JobRetryPolicy? GetRetryPolicy(string jobTypeFqn) => null;
        public string? GetContinuationJobType(string jobTypeFqn) => null;
        public int GetPriority(string jobTypeFqn) => 0;
        public int GetMaxConcurrency(string jobTypeFqn) => 0;
    }
}
