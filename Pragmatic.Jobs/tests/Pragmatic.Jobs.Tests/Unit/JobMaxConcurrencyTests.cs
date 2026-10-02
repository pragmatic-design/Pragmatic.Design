using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.Services;
using Pragmatic.Jobs.Stores;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Jobs.Tests.Unit;

/// <summary>
///     Drives the real <see cref="JobProcessorService"/> to verify that <c>[Job(MaxConcurrency = n)]</c>
///     bounds how many instances of one job type run at once on a host, independent of the global
///     worker count.
/// </summary>
/// <remarks>
///     ⚠️ Driven one poll at a time — <c>PollPendingAsync</c>, then <c>TryStart</c> per job, as the loop
///     does — and not through <c>StartAsync</c>. Through the hosted service the test would also assert that
///     the polling loop gets a thread promptly, which fails under a loaded gate — waiting for the
///     first start, or for both to complete — while the same test is green alone in four seconds.
///     The cap is decided when
///     <c>TryStart</c> reserves the slot, synchronously, so nothing here waits on a deadline.
/// </remarks>
public class JobMaxConcurrencyTests
{
    /// <summary>The longest any single step may take before the test is declared hung.</summary>
    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task MaxConcurrencyOne_HoldsTheSecondInstanceBack_UntilTheFirstFreesItsSlot()
    {
        var registry = new GatingRegistry { MaxConcurrency = 1 };
        var clock = SystemClock.Instance;
        var jobStore = new InMemoryJobStore(clock);

        // Two due jobs of the same capped type; four workers, so without the cap both would run.
        for (var i = 0; i < 2; i++)
        {
            await jobStore.EnqueueAsync(new JobInstance
            {
                Id = Guid.NewGuid(),
                JobType = "TestApp.BlockingJob",
                Status = JobStatus.Pending,
                ScheduledFor = DateTimeOffset.UtcNow.AddMinutes(-1),
                MaxAttempts = 1
            });
        }

        var services = new ServiceCollection();
        services.AddSingleton<IJobStore>(jobStore);
        services.AddSingleton<IJobTypeRegistry>(registry);
        services.AddSingleton<IClock>(clock);
        var provider = services.BuildServiceProvider();

        var processor = new JobProcessorService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            clock,
            new JobsOptions { WorkerCount = 4, PollingIntervalSeconds = 1, StartupDelaySeconds = 0 },
            NullLogger<JobProcessorService>.Instance);

        var pending = await processor.PollPendingAsync(CancellationToken.None);
        pending.Should().HaveCount(2);

        var first = processor.TryStart(pending[0], static () => { }, CancellationToken.None);
        var heldBack = processor.TryStart(pending[1], static () => { }, CancellationToken.None);

        first.Should().NotBeNull("a free slot starts the first instance");
        heldBack.Should().BeNull("the second instance is held back by the cap, with workers to spare");

        // The first body blocks on the gate until released; then it completes and frees its slot.
        registry.Gate.Release(10);
        await first!.WaitAsync(HangGuard);

        var second = processor.TryStart(pending[1], static () => { }, CancellationToken.None);
        second.Should().NotBeNull("the first instance freed its slot");
        await second!.WaitAsync(HangGuard);

        (await jobStore.GetAsync(pending[0].Id))!.Status.Should().Be(JobStatus.Completed);
        (await jobStore.GetAsync(pending[1].Id))!.Status.Should().Be(JobStatus.Completed);
        registry.MaxObserved.Should().Be(1, "never more than one instance ran at once");
    }

    // Executes job bodies under test control: each running body blocks on a gate so the test can
    // observe how many run at once.
    private sealed class GatingRegistry : IJobTypeRegistry
    {
        public int MaxConcurrency { get; init; }
        public int Running;
        public int MaxObserved;
        public readonly SemaphoreSlim Gate = new(0);
        private readonly Lock _lock = new();

        public async Task ExecuteAsync(string jobTypeFqn, string? parametersJson, JobContext context,
            IServiceProvider serviceProvider, CancellationToken ct)
        {
            var running = System.Threading.Interlocked.Increment(ref Running);
            lock (_lock)
                MaxObserved = Math.Max(MaxObserved, running);
            try
            {
                await Gate.WaitAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                System.Threading.Interlocked.Decrement(ref Running);
            }
        }

        public int GetMaxConcurrency(string jobTypeFqn) => MaxConcurrency;
        // This fake stands in for a generated registry, and a generated one knows the jobs of its
        // assembly. Answering false would make the scheduler refuse what these tests schedule — which
        // is the refusal working, not a failure of theirs.
        public bool Knows(string jobTypeFqn) => true;

        public object? DeserializeParameters(string jobTypeFqn, string? json) => null;
        public string? SerializeParameters(string jobTypeFqn, object? parameters) => null;
        public JobRetryPolicy? GetRetryPolicy(string jobTypeFqn) => null;
        public string? GetContinuationJobType(string jobTypeFqn) => null;
        public int GetPriority(string jobTypeFqn) => 0;
    }
}
