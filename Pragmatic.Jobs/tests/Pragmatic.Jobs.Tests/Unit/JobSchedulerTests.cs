using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.Services;
using Pragmatic.Jobs.Stores;
using Pragmatic.Temporal.Testing;
using Xunit;

namespace Pragmatic.Jobs.Tests.Unit;

public class JobSchedulerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static (JobScheduler scheduler, InMemoryJobStore store) CreateSut(JobsOptions? options = null)
    {
        var store = new InMemoryJobStore(new TestClock(Now));
        var scheduler = new JobScheduler(
            store,
            new TestClock(Now),
            options ?? new JobsOptions(),
            NullLogger<JobScheduler>.Instance,
            Pragmatic.Serialization.PragmaticJsonOptions.Default,
            // ⚠️ Not an empty registry: the scheduler refuses a type no registry
            // knows, because a row scheduled for one can never run. These tests are about what a
            // scheduled row looks like, so the registry says it knows what they schedule — which is
            // what a generated one does for the jobs of its own assembly.
            new KnowsEverything());
        return (scheduler, store);
    }

    [Fact]
    public async Task ScheduleAsync_WithoutDelay_SchedulesForNow()
    {
        var (scheduler, store) = CreateSut();

        var id = await scheduler.ScheduleAsync<SampleJob>();

        var job = await store.GetAsync(id);
        job.Should().NotBeNull();
        job!.ScheduledFor.Should().Be(Now);
        job.Status.Should().Be(JobStatus.Pending);
    }

    [Fact]
    public async Task ScheduleAsync_WithDelay_SchedulesForNowPlusDelay()
    {
        var (scheduler, store) = CreateSut();

        var id = await scheduler.ScheduleAsync<SampleJob>(delay: TimeSpan.FromMinutes(30));

        var job = await store.GetAsync(id);
        job!.ScheduledFor.Should().Be(Now.AddMinutes(30));
    }

    [Fact]
    public async Task ScheduleAsync_SetsJobTypeToFullName()
    {
        var (scheduler, store) = CreateSut();

        var id = await scheduler.ScheduleAsync<SampleJob>();

        var job = await store.GetAsync(id);
        job!.JobType.Should().Be(typeof(SampleJob).FullName);
    }

    [Fact]
    public async Task ScheduleAsync_AppliesDefaultMaxRetriesFromOptions()
    {
        var (scheduler, store) = CreateSut(new JobsOptions { DefaultMaxRetries = 5 });

        var id = await scheduler.ScheduleAsync<SampleJob>();

        var job = await store.GetAsync(id);
        job!.MaxAttempts.Should().Be(5);
    }

    [Fact]
    public async Task ScheduleAsync_PropagatesCorrelationId()
    {
        var (scheduler, store) = CreateSut();

        var id = await scheduler.ScheduleAsync<SampleJob>(correlationId: "corr-123");

        var job = await store.GetAsync(id);
        job!.CorrelationId.Should().Be("corr-123");
    }

    [Fact]
    public async Task ScheduleAsync_SetsScheduledForFromInjectedClock()
    {
        var (scheduler, store) = CreateSut();

        var id = await scheduler.ScheduleAsync<SampleJob>();

        var job = await store.GetAsync(id);
        // Both derive from the injected clock: a CreatedAt stamped from the wall clock could
        // disagree with the ScheduledFor next to it.
        job!.ScheduledFor.Should().Be(Now);
        job.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task ScheduleAsync_WithParameters_SerializesParametersAndType()
    {
        var (scheduler, store) = CreateSut();

        var id = await scheduler.ScheduleAsync<SampleJobWithParams, SampleParams>(new SampleParams { Value = 99 });

        var job = await store.GetAsync(id);
        job!.ParametersJson.Should().Contain("99");
        job.ParameterType.Should().Be(typeof(SampleParams).FullName);
    }

    [Fact]
    public async Task ScheduleAsync_WithParameters_UsesCamelCasePropertyNaming()
    {
        var (scheduler, store) = CreateSut();

        var id = await scheduler.ScheduleAsync<SampleJobWithParams, SampleParams>(new SampleParams { Value = 7 });

        var job = await store.GetAsync(id);
        job!.ParametersJson.Should().Contain("\"value\"");
    }

    [Fact]
    public async Task ScheduleAtAsync_UsesAbsoluteTimestamp()
    {
        var (scheduler, store) = CreateSut();
        var when = Now.AddDays(3);

        var id = await scheduler.ScheduleAtAsync<SampleJob>(when);

        var job = await store.GetAsync(id);
        job!.ScheduledFor.Should().Be(when);
    }

    [Fact]
    public async Task ScheduleAtAsync_WithParameters_SerializesParametersAndSetsTime()
    {
        var (scheduler, store) = CreateSut();
        var when = Now.AddHours(6);

        var id = await scheduler.ScheduleAtAsync<SampleJobWithParams, SampleParams>(new SampleParams { Value = 1 }, when);

        var job = await store.GetAsync(id);
        job!.ScheduledFor.Should().Be(when);
        job.ParametersJson.Should().Contain("1");
        job.ParameterType.Should().Be(typeof(SampleParams).FullName);
    }

    [Fact]
    public async Task ScheduleAsync_ReturnsUniqueIds()
    {
        var (scheduler, _) = CreateSut();

        var first = await scheduler.ScheduleAsync<SampleJob>();
        var second = await scheduler.ScheduleAsync<SampleJob>();

        first.Should().NotBe(second);
    }

    [Fact]
    public async Task CancelAsync_MarksJobCancelled()
    {
        var (scheduler, store) = CreateSut();
        var id = await scheduler.ScheduleAsync<SampleJob>();

        await scheduler.CancelAsync(id);

        var job = await store.GetAsync(id);
        job!.Status.Should().Be(JobStatus.Cancelled);
        job.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CancelAsync_UnknownId_DoesNotThrow()
    {
        var (scheduler, _) = CreateSut();

        var act = () => scheduler.CancelAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }

    // JobContinuation was a public, documented factory that no API accepted: JobProcessorService reads
    // ContinuationJobType/ContinuationParametersJson off the job row, and nothing ever wrote them from a
    // continuation. Asserted on the persisted row, because that is what the processor reads — a test
    // that only checked the parameter arrives would pass for a continuation nobody enqueues.
    [Fact]
    public async Task ScheduleAsync_WithContinuation_PersistsItOnTheJobRow()
    {
        var (scheduler, store) = CreateSut();

        var id = await scheduler.ScheduleAsync<SampleJob>(
            continuation: JobContinuation.Then<SampleJobWithParams, SampleParams>(new SampleParams { Value = 7 }));

        var job = await store.GetAsync(id);
        job.Should().NotBeNull();
        job!.ContinuationJobType.Should().Be(typeof(SampleJobWithParams).FullName);
        job.ContinuationParametersJson.Should().Contain("7");
    }

    [Fact]
    public async Task ScheduleAsync_WithoutContinuation_LeavesTheColumnsEmpty()
    {
        var (scheduler, store) = CreateSut();

        var id = await scheduler.ScheduleAsync<SampleJob>();

        var job = await store.GetAsync(id);
        job!.ContinuationJobType.Should().BeNull();
        job.ContinuationParametersJson.Should().BeNull();
    }

    private sealed class SampleJob : IJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class SampleJobWithParams : IJob<SampleParams>
    {
        public Task ExecuteAsync(SampleParams parameters, JobContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class SampleParams
    {
        public int Value { get; set; }
    }
}
