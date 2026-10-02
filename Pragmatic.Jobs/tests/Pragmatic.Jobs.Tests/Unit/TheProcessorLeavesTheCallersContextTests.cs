using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.Services;
using Pragmatic.Jobs.Stores;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Jobs.Tests.Unit;

/// <summary>
///     The processor's loop runs on the thread pool, not on whatever context started it.
/// </summary>
/// <remarks>
///     <para>
///         <c>BackgroundService.StartAsync</c> returns at <c>ExecuteAsync</c>'s first await, and the
///         caller's synchronization context is current at that moment — so a loop that resumed on it
///         would run at the mercy of whoever started the host. In an xUnit assembly that caller is
///         <c>MaxConcurrencySyncContext</c>, shared with every other test in the process.
///     </para>
///     <para>
///         ⚠️ Written while ruling that out as the cause of a flaky gate — red on
///         <c>JobTenantRestoreTests</c> in five of seven runs — and it ruled it out: the loop already
///         leaves the caller's context, this passed the first time it ran, and the cause of that
///         timeout is still unexplained. It stays because the property is worth a control: a hostile
///         caller context is one line away from being possible again, and nothing else says so.
///     </para>
///     <para>
///         The context here swallows what is posted to it, so a regression is a <b>hang</b> rather
///         than a slowdown: this would fail in seconds and deterministically.
///     </para>
/// </remarks>
public class TheProcessorLeavesTheCallersContextTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WithAContextThatRunsNothing_TheJobStillRuns()
    {
        var clock = new TestClock(Now);
        var store = new InMemoryJobStore(clock);
        await store.EnqueueAsync(new JobInstance
        {
            Id = Guid.NewGuid(),
            JobType = "TestApp.AnyJob",
            Status = JobStatus.Pending,
            ScheduledFor = Now.AddMinutes(-1),
            MaxAttempts = 1
        });

        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new RanOnceRegistry(ran);

        var services = new ServiceCollection();
        services.AddSingleton<IJobStore>(store);
        services.AddSingleton<IJobTypeRegistry>(registry);
        services.AddSingleton<IClock>(clock);
        var provider = services.BuildServiceProvider();

        var processor = new JobProcessorService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            clock,
            new JobsOptions { WorkerCount = 1, PollingIntervalSeconds = 1, StartupDelaySeconds = 0 },
            NullLogger<JobProcessorService>.Instance);

        var swallowed = new SwallowingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(swallowed);
        try
        {
            await processor.StartAsync(CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        // Five seconds, and it is not a timing assertion: with the loop posted to a context that runs
        // nothing, no amount of waiting would ever complete this.
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        await processor.StopAsync(CancellationToken.None).ConfigureAwait(true);

        swallowed.Posted.Should().Be(0,
            "a background loop that schedules itself on its caller runs at the caller's mercy");
    }

    /// <summary>A context that accepts work and never runs it — a caller that is busy forever.</summary>
    private sealed class SwallowingSynchronizationContext : SynchronizationContext
    {
        private int _posted;

        public int Posted => Volatile.Read(ref _posted);

        public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _posted);

        public override void Send(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _posted);
    }

    /// <summary>Completes the first time a job body runs.</summary>
    private sealed class RanOnceRegistry(TaskCompletionSource ran) : IJobTypeRegistry
    {
        public Task ExecuteAsync(string jobTypeFqn, string? parametersJson, JobContext context,
            IServiceProvider serviceProvider, CancellationToken ct)
        {
            ran.TrySetResult();
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
