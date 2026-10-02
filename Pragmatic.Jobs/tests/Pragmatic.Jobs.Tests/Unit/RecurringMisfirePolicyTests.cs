using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Jobs.Attributes;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.Services;
using Pragmatic.Jobs.Stores;
using Pragmatic.Temporal.Testing;

namespace Pragmatic.Jobs.Tests.Unit;

/// <summary>
///     Drives the real <see cref="RecurringJobSchedulerService"/> to verify how a missed occurrence
///     (host was down past the misfire threshold) is handled per its <see cref="MisfirePolicy"/>.
/// </summary>
public class RecurringMisfirePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     The scheduler and its two stores, seeded with a definition whose occurrence is a misfire.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Built, not started. The tests drive <c>RunDuePassAsync</c> and assert once it returns.
    ///     Starting the hosted service and polling for its effect means outlasting a two-second
    ///     startup delay and a one-second poll interval, and under the gate's concurrent suites even a
    ///     thirty-second budget is not always enough: such a test goes red with nothing wrong in the
    ///     scheduler.
    /// </remarks>
    private static async Task<(InMemoryJobStore Jobs, InMemoryRecurringJobStore Recurring, RecurringJobSchedulerService Scheduler)>
        BuildSchedulerAsync(MisfirePolicy policy, DateTimeOffset missedOccurrence)
    {
        var clock = new TestClock(Now);
        var jobStore = new InMemoryJobStore(clock);
        var recurringStore = new InMemoryRecurringJobStore();

        // Seed a definition whose occurrence is already well in the past — a misfire.
        await recurringStore.UpsertAsync(new RecurringJobDefinition
        {
            Id = "hourly",
            JobType = "TestApp.HourlyJob",
            CronExpression = "0 * * * *",
            NextExecutionAt = missedOccurrence,
            IsEnabled = true,
            MisfirePolicy = policy
        }).ConfigureAwait(false);

        var services = new ServiceCollection();
        services.AddSingleton<IJobStore>(jobStore);
        services.AddSingleton<IRecurringJobStore>(recurringStore);
        services.AddSingleton<IJobTypeRegistry>(new StubJobTypeRegistry());
        services.AddSingleton<Pragmatic.Temporal.Clock.IClock>(clock);
        var provider = services.BuildServiceProvider();

        var scheduler = new RecurringJobSchedulerService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StubJobTypeRegistry(),
            clock,
            new JobsOptions { PollingIntervalSeconds = 1, MisfireThreshold = TimeSpan.FromMinutes(1) },
            NullLogger<RecurringJobSchedulerService>.Instance);

        return (jobStore, recurringStore, scheduler);
    }

    [Fact]
    public async Task RunOnce_OnMisfire_EnqueuesTheMissedOccurrence()
    {
        // Missed occurrence one hour ago; the scheduler's own clock is fixed at Now.
        var missed = Now.AddHours(-1);
        var (jobStore, recurring, scheduler) = await BuildSchedulerAsync(MisfirePolicy.RunOnce, missed).ConfigureAwait(true);

        await scheduler.RunDuePassAsync(CancellationToken.None).ConfigureAwait(true);

        jobStore.Count.Should().Be(1, "RunOnce must enqueue the missed occurrence once");

        var job = (await jobStore.GetPendingAsync(10, Now).ConfigureAwait(true)).Single();
        job.JobType.Should().Be("TestApp.HourlyJob");
        job.ScheduledFor.Should().Be(missed, "the enqueued instance carries the missed occurrence time");

        // And the schedule advanced past the misfire.
        var def = await recurring.GetAsync("hourly").ConfigureAwait(true);
        def!.NextExecutionAt.Should().BeAfter(Now);
    }

    [Fact]
    public async Task Skip_OnMisfire_DoesNotEnqueueButAdvancesTheSchedule()
    {
        var missed = Now.AddHours(-1);
        var (jobStore, recurring, scheduler) = await BuildSchedulerAsync(MisfirePolicy.Skip, missed).ConfigureAwait(true);

        await scheduler.RunDuePassAsync(CancellationToken.None).ConfigureAwait(true);

        // The schedule moved forward — the proof the scheduler processed the definition at all, and
        // what makes the assertion below mean "did not enqueue" rather than "did not run yet".
        var def = await recurring.GetAsync("hourly").ConfigureAwait(true);
        def!.NextExecutionAt.Should().BeAfter(Now, "the scheduler must claim and advance the due definition");

        // ...but no job instance was enqueued for the stale occurrence.
        jobStore.Count.Should().Be(0, "Skip must not run a missed occurrence");
    }

    private sealed class StubJobTypeRegistry : IJobTypeRegistry
    {
        // Non-null so the scheduler does not early-return; the scheduler only enqueues, it never
        // dispatches, so execution members are unreachable here.
        // This fake stands in for a generated registry, and a generated one knows the jobs of its
        // assembly. Answering false would make the scheduler refuse what these tests schedule — which
        // is the refusal working, not a failure of theirs.
        public bool Knows(string jobTypeFqn) => true;

        public object? DeserializeParameters(string jobTypeFqn, string? json) => null;
        public string? SerializeParameters(string jobTypeFqn, object? parameters) => null;
        public Task ExecuteAsync(string jobTypeFqn, string? parametersJson, JobContext context,
            IServiceProvider serviceProvider, CancellationToken ct) => Task.CompletedTask;
        public JobRetryPolicy? GetRetryPolicy(string jobTypeFqn) => null;
        public string? GetContinuationJobType(string jobTypeFqn) => null;
        public int GetPriority(string jobTypeFqn) => 0;
        public int GetMaxConcurrency(string jobTypeFqn) => 0;
    }
}
