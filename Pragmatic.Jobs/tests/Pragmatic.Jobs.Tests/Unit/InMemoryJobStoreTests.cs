using Pragmatic.Testing.Assertions;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Stores;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Jobs.Tests.Unit;

public class InMemoryJobStoreTests
{
    private const string Worker = "worker-1";

    private readonly InMemoryJobStore _store = new(SystemClock.Instance);

    private static JobInstance CreateJob(string type = "TestJob", TimeSpan? delay = null) => new()
    {
        Id = Guid.NewGuid(),
        JobType = type,
        Status = JobStatus.Pending,
        ScheduledFor = DateTimeOffset.UtcNow.Add(delay ?? TimeSpan.Zero),
        MaxAttempts = 3,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task EnqueueAsync_SetsStatusToPending()
    {
        var job = CreateJob();
        var result = await _store.EnqueueAsync(job);

        result.Status.Should().Be(JobStatus.Pending);
        _store.Count.Should().Be(1);
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsDueJobsOnly()
    {
        var due = CreateJob();
        var future = CreateJob(delay: TimeSpan.FromHours(1));

        await _store.EnqueueAsync(due);
        await _store.EnqueueAsync(future);

        var pending = await _store.GetPendingAsync(10, DateTimeOffset.UtcNow);

        pending.Should().HaveCount(1);
        pending[0].Id.Should().Be(due.Id);
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_AcquiresSuccessfully()
    {
        var job = CreateJob();
        await _store.EnqueueAsync(job);

        var acquired = await _store.TryAcquireLeaseAsync(job.Id, "worker-1", TimeSpan.FromMinutes(5));

        acquired.Should().BeTrue();
        var updated = await _store.GetAsync(job.Id);
        updated!.Status.Should().Be(JobStatus.Running);
        updated.LeasedBy.Should().Be("worker-1");
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_FailsWhenAlreadyLeased()
    {
        var job = CreateJob();
        await _store.EnqueueAsync(job);
        await _store.TryAcquireLeaseAsync(job.Id, "worker-1", TimeSpan.FromMinutes(5));

        var acquired = await _store.TryAcquireLeaseAsync(job.Id, "worker-2", TimeSpan.FromMinutes(5));

        acquired.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_ConcurrentWorkers_OnlyOneSucceeds()
    {
        // Regression (Fase 0.2): without the lock around check+acquire,
        // multiple workers can observe "not leased" and all write, causing
        // duplicate execution. The barrier maximises contention.
        var job = CreateJob();
        await _store.EnqueueAsync(job);

        const int workerCount = 32;
        using var barrier = new Barrier(workerCount);
        var tasks = new Task<bool>[workerCount];
        for (var i = 0; i < workerCount; i++)
        {
            var workerId = $"worker-{i}";
            tasks[i] = Task.Run(() =>
            {
                barrier.SignalAndWait();
                return _store.TryAcquireLeaseAsync(job.Id, workerId, TimeSpan.FromMinutes(5));
            });
        }

        var results = await Task.WhenAll(tasks);
        results.Count(x => x).Should().Be(1, "lease acquisition must be atomic");
    }

    [Fact]
    public async Task MarkCompletedAsync_SetsStatusAndTimestamp()
    {
        var job = CreateJob();
        await _store.EnqueueAsync(job);
        await _store.TryAcquireLeaseAsync(job.Id, Worker, TimeSpan.FromMinutes(5));

        await _store.MarkCompletedAsync(job.Id, Worker);

        var updated = await _store.GetAsync(job.Id);
        updated!.Status.Should().Be(JobStatus.Completed);
        updated.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkFailedAsync_ResetsToPending_WhenAttemptsRemaining()
    {
        var job = CreateJob();
        await _store.EnqueueAsync(job);
        await _store.TryAcquireLeaseAsync(job.Id, Worker, TimeSpan.FromMinutes(5));

        await _store.MarkFailedAsync(job.Id, Worker, "timeout", attempt: 1, retryDelay: TimeSpan.Zero);

        var updated = await _store.GetAsync(job.Id);
        updated!.Status.Should().Be(JobStatus.Pending);
        updated.Attempt.Should().Be(1);
        updated.Error.Should().Be("timeout");
    }

    [Fact]
    public async Task MarkFailedAsync_SetsStatusToFailed_WhenExhausted()
    {
        var job = CreateJob();
        await _store.EnqueueAsync(job);
        await _store.TryAcquireLeaseAsync(job.Id, Worker, TimeSpan.FromMinutes(5));

        await _store.MarkFailedAsync(job.Id, Worker, "permanent failure", attempt: 3, retryDelay: TimeSpan.Zero);

        var updated = await _store.GetAsync(job.Id);
        updated!.Status.Should().Be(JobStatus.Failed);
        updated.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkCancelledAsync_SetsStatus()
    {
        var job = CreateJob();
        await _store.EnqueueAsync(job);

        await _store.MarkCancelledAsync(job.Id);

        var updated = await _store.GetAsync(job.Id);
        updated!.Status.Should().Be(JobStatus.Cancelled);
    }

    [Fact]
    public async Task ReleaseExpiredLeasesAsync_ResetsExpiredTooPending()
    {
        var job = CreateJob();
        await _store.EnqueueAsync(job);
        await _store.TryAcquireLeaseAsync(job.Id, "worker-1", TimeSpan.FromMilliseconds(1));

        await Task.Delay(10); // Let lease expire
        await _store.ReleaseExpiredLeasesAsync(DateTimeOffset.UtcNow);

        var updated = await _store.GetAsync(job.Id);
        updated!.Status.Should().Be(JobStatus.Pending);
        updated.LeasedBy.Should().BeNull();
    }

    [Fact]
    public async Task RenewLeaseAsync_ExtendsExpiration()
    {
        var job = CreateJob();
        await _store.EnqueueAsync(job);
        await _store.TryAcquireLeaseAsync(job.Id, "worker-1", TimeSpan.FromMinutes(1));

        var before = (await _store.GetAsync(job.Id))!.LeaseExpiresAt;
        await Task.Delay(10);
        await _store.RenewLeaseAsync(job.Id, "worker-1", TimeSpan.FromMinutes(10));
        var after = (await _store.GetAsync(job.Id))!.LeaseExpiresAt;

        after.Should().BeAfter(before!.Value);
    }
}
