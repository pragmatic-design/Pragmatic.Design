using Pragmatic.Testing.Assertions;
using Pragmatic.Jobs.Stores;
using Pragmatic.Temporal.Testing;

namespace Pragmatic.Jobs.Tests.Unit;

/// <summary>
///     Without retention the job table grows by one row per execution forever, and every row keeps
///     its serialized parameters — which commonly carry personal data.
/// </summary>
public class JobRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private const string Worker = "worker-1";

    private static InMemoryJobStore CreateStore() => new(new TestClock(Now));

    private static JobInstance Job(JobStatus status = JobStatus.Pending) => new()
    {
        Id = Guid.NewGuid(),
        JobType = "TestApp.TestJob",
        Status = status,
        ScheduledFor = Now,
        MaxAttempts = 3
    };

    private static async Task<JobInstance> CompletedJobAsync(InMemoryJobStore store)
    {
        var job = await store.EnqueueAsync(Job()).ConfigureAwait(false);
        await store.TryAcquireLeaseAsync(job.Id, Worker, TimeSpan.FromMinutes(5)).ConfigureAwait(false);
        await store.MarkCompletedAsync(job.Id, Worker).ConfigureAwait(false);
        return job;
    }

    [Fact]
    public async Task PurgeTerminalAsync_RemovesJobsFinishedBeforeCutoff()
    {
        var store = CreateStore();
        var job = await CompletedJobAsync(store);

        var deleted = await store.PurgeTerminalAsync(Now.AddDays(1), batchSize: 100);

        deleted.Should().Be(1);
        (await store.GetAsync(job.Id)).Should().BeNull();
    }

    [Fact]
    public async Task PurgeTerminalAsync_KeepsJobsFinishedAfterCutoff()
    {
        var store = CreateStore();
        var job = await CompletedJobAsync(store);

        var deleted = await store.PurgeTerminalAsync(Now.AddDays(-1), batchSize: 100);

        deleted.Should().Be(0);
        (await store.GetAsync(job.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task PurgeTerminalAsync_NeverRemovesUnfinishedWork()
    {
        var store = CreateStore();
        var pending = await store.EnqueueAsync(Job());
        var running = await store.EnqueueAsync(Job());
        await store.TryAcquireLeaseAsync(running.Id, Worker, TimeSpan.FromMinutes(5));

        var deleted = await store.PurgeTerminalAsync(Now.AddYears(1), batchSize: 100);

        deleted.Should().Be(0, "a job that has not finished must never be purged, however old");
        (await store.GetAsync(pending.Id)).Should().NotBeNull();
        (await store.GetAsync(running.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task PurgeTerminalAsync_RespectsBatchSize()
    {
        var store = CreateStore();
        for (var i = 0; i < 5; i++)
            await CompletedJobAsync(store);

        var deleted = await store.PurgeTerminalAsync(Now.AddDays(1), batchSize: 2);

        deleted.Should().Be(2);
        store.Count.Should().Be(3);
    }

    [Fact]
    public async Task PurgeTerminalAsync_RemovesCancelledAndFailedToo()
    {
        var store = CreateStore();

        var cancelled = await store.EnqueueAsync(Job());
        await store.MarkCancelledAsync(cancelled.Id);

        var failed = await store.EnqueueAsync(Job());
        await store.TryAcquireLeaseAsync(failed.Id, Worker, TimeSpan.FromMinutes(5));
        await store.MarkFailedAsync(failed.Id, Worker, "boom", attempt: 3, retryDelay: TimeSpan.Zero);

        var deleted = await store.PurgeTerminalAsync(Now.AddDays(1), batchSize: 100);

        deleted.Should().Be(2);
    }
}
