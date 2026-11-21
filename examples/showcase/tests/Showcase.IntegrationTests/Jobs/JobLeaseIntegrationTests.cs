using Pragmatic.Testing.Assertions;
using Pragmatic.Jobs;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Jobs;

/// <summary>
///     Exercises the lease protocol of the durable job store against a real database: <c>EfCoreJobStore</c> on EF Core, the engine's own
///     store on each engine (<see cref="JobStoreUnderTest" />, #904).
/// </summary>
/// <remarks>
///     Every one of these paths is a conditional update, which the SQLite-based unit suite cannot run against EF Core, so those tests are
///     skipped there. This is the store's whole purpose — distributed execution without duplicates — so it is covered here on the database it
///     targets, including a genuinely parallel acquisition race.
/// </remarks>
public class JobLeaseIntegrationTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private JobStoreUnderTest JobStore => new(Fixture);

    private static async Task<JobInstance> EnqueueAsync(IJobStore store)
    {
        var job = new JobInstance
        {
            Id = Guid.NewGuid(),
            JobType = "IntegrationTest.LeasedJob",
            ScheduledFor = DateTimeOffset.UtcNow.AddMinutes(-1),
            MaxAttempts = 3,
            Status = JobStatus.Pending
        };
        return await store.EnqueueAsync(job);
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_WhenUnleased_SucceedsAndMarksRunning()
    {
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Jobs;
        var job = await EnqueueAsync(store);

        var acquired = await store.TryAcquireLeaseAsync(job.Id, "worker-a", Lease);

        acquired.Should().BeTrue();
        var stored = await store.GetAsync(job.Id);
        stored!.Status.Should().Be(JobStatus.Running);
        stored.LeasedBy.Should().Be("worker-a");
        stored.LeaseExpiresAt.Should().NotBeNull();
        stored.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_WhenHeldByAnotherWorker_Fails()
    {
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Jobs;
        var job = await EnqueueAsync(store);
        await store.TryAcquireLeaseAsync(job.Id, "worker-a", Lease);

        var second = await store.TryAcquireLeaseAsync(job.Id, "worker-b", Lease);

        second.Should().BeFalse("a live lease must not be stealable");
        (await store.GetAsync(job.Id))!.LeasedBy.Should().Be("worker-a");
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_UnderConcurrency_ExactlyOneWorkerWins()
    {
        // A single-threaded test cannot surface a claim race: uniqueness defects only appear under
        // a genuinely parallel gate, each contender on its own connection.
        const int workers = 16;
        await using var setup = await JobStore.OpenAsync();
        var job = await EnqueueAsync(setup.Jobs);

        using var barrier = new Barrier(workers);

        // A dedicated thread per contender: the barrier blocks every one of them, and on pool threads
        // that starves the pool for every other test in the process while it grows to fit them.
        var tasks = Enumerable.Range(0, workers).Select(i => Task.Factory.StartNew(async () =>
        {
            await using var stores = await JobStore.OpenAsync().ConfigureAwait(false);
            barrier.SignalAndWait();
            return await stores.Jobs.TryAcquireLeaseAsync(job.Id, $"worker-{i}", Lease).ConfigureAwait(false);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap());

        var results = await Task.WhenAll(tasks);

        results.Count(won => won).Should().Be(1, "exactly one worker may acquire the lease");
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_WhenAlreadyCompleted_Fails()
    {
        // MarkCompletedAsync clears LeasedBy. If the acquire predicate does not also exclude
        // terminal states, a completed job is re-acquired and executed a second time.
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Jobs;
        var job = await EnqueueAsync(store);
        await store.TryAcquireLeaseAsync(job.Id, "worker-a", Lease);
        await store.MarkCompletedAsync(job.Id, "worker-a");

        var reacquired = await store.TryAcquireLeaseAsync(job.Id, "worker-b", Lease);

        reacquired.Should().BeFalse("a completed job must never be executed again");
        (await store.GetAsync(job.Id))!.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_WhenCancelled_Fails()
    {
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Jobs;
        var job = await EnqueueAsync(store);
        await store.MarkCancelledAsync(job.Id);

        var acquired = await store.TryAcquireLeaseAsync(job.Id, "worker-a", Lease);

        acquired.Should().BeFalse("cancelling a pending job must actually prevent it from running");
    }

    [Fact]
    public async Task MarkCompletedAsync_ByNonHolder_DoesNotCompleteTheJob()
    {
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Jobs;
        var job = await EnqueueAsync(store);
        await store.TryAcquireLeaseAsync(job.Id, "worker-a", Lease);

        await store.MarkCompletedAsync(job.Id, "worker-b");

        (await store.GetAsync(job.Id))!.Status.Should().Be(JobStatus.Running,
            "only the lease holder may declare the job finished");
    }

    [Fact]
    public async Task RenewLeaseAsync_ByHolder_ExtendsExpiry()
    {
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Jobs;
        var job = await EnqueueAsync(store);
        await store.TryAcquireLeaseAsync(job.Id, "worker-a", TimeSpan.FromMinutes(1));
        var before = (await store.GetAsync(job.Id))!.LeaseExpiresAt;

        await store.RenewLeaseAsync(job.Id, "worker-a", TimeSpan.FromMinutes(30));

        (await store.GetAsync(job.Id))!.LeaseExpiresAt.Should().BeAfter(before!.Value);
    }

    [Fact]
    public async Task RenewLeaseAsync_ByNonHolder_DoesNotExtend()
    {
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Jobs;
        var job = await EnqueueAsync(store);
        await store.TryAcquireLeaseAsync(job.Id, "worker-a", TimeSpan.FromMinutes(1));
        var before = (await store.GetAsync(job.Id))!.LeaseExpiresAt;

        await store.RenewLeaseAsync(job.Id, "worker-b", TimeSpan.FromMinutes(30));

        (await store.GetAsync(job.Id))!.LeaseExpiresAt.Should().Be(before, "only the lease holder may renew");
    }

    [Fact]
    public async Task ReleaseExpiredLeasesAsync_ReturnsOrphanedJobToPendingAndCountsAnAttempt()
    {
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Jobs;
        var job = await EnqueueAsync(store);
        await store.TryAcquireLeaseAsync(job.Id, "crashed-worker", TimeSpan.FromSeconds(1));

        // Simulate the worker dying: sweep with a clock past the lease expiry.
        await store.ReleaseExpiredLeasesAsync(DateTimeOffset.UtcNow.AddMinutes(1));

        var stored = await store.GetAsync(job.Id);
        stored!.Status.Should().Be(JobStatus.Pending);
        stored.LeasedBy.Should().BeNull();
        stored.Attempt.Should().Be(1,
            "a crash must consume an attempt, otherwise a job that kills its worker retries forever");
    }

    [Fact]
    public async Task ReleaseExpiredLeasesAsync_WhenAttemptsExhausted_FailsTheJob()
    {
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Jobs;
        var job = new JobInstance
        {
            Id = Guid.NewGuid(),
            JobType = "IntegrationTest.PoisonJob",
            ScheduledFor = DateTimeOffset.UtcNow.AddMinutes(-1),
            MaxAttempts = 1,
            Status = JobStatus.Pending
        };
        await store.EnqueueAsync(job);
        await store.TryAcquireLeaseAsync(job.Id, "crashed-worker", TimeSpan.FromSeconds(1));

        await store.ReleaseExpiredLeasesAsync(DateTimeOffset.UtcNow.AddMinutes(1));

        (await store.GetAsync(job.Id))!.Status.Should().Be(JobStatus.Failed);
    }

    [Fact]
    public async Task ReleaseExpiredLeasesAsync_DoesNotTouchLiveLease()
    {
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Jobs;
        var job = await EnqueueAsync(store);
        await store.TryAcquireLeaseAsync(job.Id, "worker-a", TimeSpan.FromMinutes(30));

        await store.ReleaseExpiredLeasesAsync(DateTimeOffset.UtcNow);

        var stored = await store.GetAsync(job.Id);
        stored!.Status.Should().Be(JobStatus.Running);
        stored.LeasedBy.Should().Be("worker-a");
    }

    [Fact]
    public async Task ReleaseLeaseAsync_ReturnsJobToPendingWithoutCountingAnAttempt()
    {
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Jobs;
        var job = await EnqueueAsync(store);
        await store.TryAcquireLeaseAsync(job.Id, "worker-a", Lease);

        await store.ReleaseLeaseAsync(job.Id, "worker-a");

        var stored = await store.GetAsync(job.Id);
        stored!.Status.Should().Be(JobStatus.Pending);
        stored.Attempt.Should().Be(0, "a graceful shutdown is not a failed attempt");
    }

    [Fact]
    public async Task MarkCompletedAsync_SetsTerminalStateAndClearsLease()
    {
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Jobs;
        var job = await EnqueueAsync(store);
        await store.TryAcquireLeaseAsync(job.Id, "worker-a", Lease);

        await store.MarkCompletedAsync(job.Id, "worker-a");

        var stored = await store.GetAsync(job.Id);
        stored!.Status.Should().Be(JobStatus.Completed);
        stored.CompletedAt.Should().NotBeNull();
        stored.LeasedBy.Should().BeNull();
    }

    [Fact]
    public async Task MarkFailedAsync_BelowMaxAttempts_ReschedulesWithBackoff()
    {
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Jobs;
        var job = await EnqueueAsync(store);
        await store.TryAcquireLeaseAsync(job.Id, "worker-a", Lease);

        await store.MarkFailedAsync(job.Id, "worker-a", "boom", attempt: 1, retryDelay: TimeSpan.FromMinutes(5));

        var stored = await store.GetAsync(job.Id);
        stored!.Status.Should().Be(JobStatus.Pending, "a retryable failure returns the job to the queue");
        stored.Attempt.Should().Be(1);
        stored.Error.Should().Contain("boom");
        stored.ScheduledFor.Should().BeAfter(DateTimeOffset.UtcNow,
            "a retry must be delayed by backoff, not re-run on the very next poll");
    }

    [Fact]
    public async Task MarkFailedAsync_AtMaxAttempts_MarksPermanentlyFailed()
    {
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Jobs;
        var job = await EnqueueAsync(store);
        await store.TryAcquireLeaseAsync(job.Id, "worker-a", Lease);

        await store.MarkFailedAsync(job.Id, "worker-a", "boom", attempt: job.MaxAttempts,
            retryDelay: TimeSpan.FromMinutes(5));

        var stored = await store.GetAsync(job.Id);
        stored!.Status.Should().Be(JobStatus.Failed);
        stored.CompletedAt.Should().NotBeNull();
        stored.LeasedBy.Should().BeNull();
    }

    [Fact]
    public async Task PurgeTerminalAsync_RemovesOldTerminalJobsOnly()
    {
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Jobs;

        var completed = await EnqueueAsync(store);
        await store.TryAcquireLeaseAsync(completed.Id, "worker-a", Lease);
        await store.MarkCompletedAsync(completed.Id, "worker-a");

        var pending = await EnqueueAsync(store);

        var deleted = await store.PurgeTerminalAsync(DateTimeOffset.UtcNow.AddMinutes(1), batchSize: 1000);

        deleted.Should().BeGreaterThan(0);
        (await store.GetAsync(completed.Id)).Should().BeNull();
        (await store.GetAsync(pending.Id)).Should().NotBeNull("unfinished work must never be purged");
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsDueJobsOnlyAndRespectsBatchSize()
    {
        // DateTimeOffset comparison in the polling predicate is the other thing SQLite cannot
        // validate: it compares timestamps lexicographically as text.
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Jobs;

        var due = await EnqueueAsync(store);
        var future = new JobInstance
        {
            Id = Guid.NewGuid(),
            JobType = "IntegrationTest.FutureJob",
            ScheduledFor = DateTimeOffset.UtcNow.AddHours(1),
            MaxAttempts = 3,
            Status = JobStatus.Pending
        };
        await store.EnqueueAsync(future);

        var pending = await store.GetPendingAsync(100, DateTimeOffset.UtcNow);

        pending.Should().Contain(j => j.Id == due.Id);
        pending.Should().NotContain(j => j.Id == future.Id,
            "a job scheduled in the future must not be polled");

        (await store.GetPendingAsync(1, DateTimeOffset.UtcNow)).Should().HaveCount(1,
            "the batch size bounds how much a single poll claims");
    }

    [Fact]
    public async Task RecurringStore_GetDueAsync_ReturnsEnabledDueDefinitionsOnly()
    {
        await using var stores = await JobStore.OpenAsync();
        var store = stores.Recurring;

        var now = DateTimeOffset.UtcNow;
        var dueId = $"due-{Guid.NewGuid():N}";
        var futureId = $"future-{Guid.NewGuid():N}";
        var disabledId = $"disabled-{Guid.NewGuid():N}";

        await store.UpsertAsync(Definition(dueId, now.AddMinutes(-1)));
        await store.UpsertAsync(Definition(futureId, now.AddHours(1)));
        var disabled = Definition(disabledId, now.AddMinutes(-1));
        await store.UpsertAsync(disabled);
        await store.DisableAsync(disabledId);

        var due = await store.GetDueAsync(now);

        due.Should().Contain(d => d.Id == dueId);
        due.Should().NotContain(d => d.Id == futureId);
        due.Should().NotContain(d => d.Id == disabledId, "a disabled definition must never be scheduled");
    }

    [Fact]
    public async Task RecurringStore_TryClaimDueAsync_OnlyOneHostWinsATick()
    {
        var id = $"claim-{Guid.NewGuid():N}";
        var occurrence = DateTimeOffset.UtcNow.AddMinutes(-1);

        DateTimeOffset? stored;
        await using (var setup = await JobStore.OpenAsync())
        {
            await setup.Recurring.UpsertAsync(Definition(id, occurrence));
            // The occurrence as the store holds it, which is what a host reads off GetDueAsync and claims with.
            stored = (await setup.Recurring.GetAsync(id))!.NextExecutionAt;
        }

        const int hosts = 8;
        using var barrier = new Barrier(hosts);

        // A dedicated thread per contender, for the reason given in the lease test above.
        var tasks = Enumerable.Range(0, hosts).Select(_ => Task.Factory.StartNew(async () =>
        {
            await using var stores = await JobStore.OpenAsync().ConfigureAwait(false);
            barrier.SignalAndWait();
            return await stores.Recurring.TryClaimDueAsync(id, stored, occurrence.AddHours(1), occurrence)
                .ConfigureAwait(false);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap());

        var results = await Task.WhenAll(tasks);

        results.Count(won => won).Should().Be(1,
            "the compare-and-swap is what stops every host enqueueing the same occurrence");
    }

    private static RecurringJobDefinition Definition(string id, DateTimeOffset nextExecution) => new()
    {
        Id = id,
        JobType = "IntegrationTest.RecurringJob",
        CronExpression = "0 * * * *",
        NextExecutionAt = nextExecution,
        IsEnabled = true
    };
}
