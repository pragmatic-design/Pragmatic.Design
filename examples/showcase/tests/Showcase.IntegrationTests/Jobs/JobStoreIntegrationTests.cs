using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Jobs;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Jobs;

/// <summary>
///     Polling and due-selection behaviour of the <see cref="IJobStore"/> and
///     <see cref="IRecurringJobStore"/> registered in the Showcase host.
/// </summary>
/// <remarks>
///     The Showcase runs jobs on the in-memory stores, so these cover the store contract as the
///     host resolves it. The EF Core implementations — and the SQL the lease protocol depends on —
///     are covered against real PostgreSQL in <see cref="JobLeaseIntegrationTests"/>.
/// </remarks>
public class JobStoreIntegrationTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // IJobStore — DateTimeOffset comparison tests
    // =========================================================================

    [Fact]
    public async Task GetPendingAsync_ReturnsDueJobsOnly()
    {
        var store = Services.GetRequiredService<IJobStore>();
        var now = DateTimeOffset.UtcNow;

        var dueJob = new JobInstance
        {
            Id = Guid.NewGuid(),
            JobType = "IntegrationTest.DueJob",
            ScheduledFor = now.AddMinutes(-1),
            MaxAttempts = 3,
            Status = JobStatus.Pending
        };

        var futureJob = new JobInstance
        {
            Id = Guid.NewGuid(),
            JobType = "IntegrationTest.FutureJob",
            ScheduledFor = now.AddMinutes(10),
            MaxAttempts = 3,
            Status = JobStatus.Pending
        };

        await store.EnqueueAsync(dueJob);
        await store.EnqueueAsync(futureJob);

        var pending = await store.GetPendingAsync(10, now);

        pending.Should().Contain(j => j.Id == dueJob.Id,
            "a job scheduled in the past should be returned as pending");
        pending.Should().NotContain(j => j.Id == futureJob.Id,
            "a job scheduled in the future should NOT be returned as pending");
    }

    [Fact]
    public async Task GetPendingAsync_RespectsLimitBatchSize()
    {
        var store = Services.GetRequiredService<IJobStore>();
        var now = DateTimeOffset.UtcNow;

        // Schedule 5 due jobs
        for (var i = 0; i < 5; i++)
        {
            await store.EnqueueAsync(new JobInstance
            {
                Id = Guid.NewGuid(),
                JobType = $"IntegrationTest.BatchJob{i}",
                ScheduledFor = now.AddMinutes(-i - 1),
                MaxAttempts = 3,
                Status = JobStatus.Pending
            });
        }

        var pending = await store.GetPendingAsync(2, now);

        pending.Should().HaveCount(2,
            "requesting a batch of 2 should return at most 2 jobs even if more are due");
    }

    // =========================================================================
    // IRecurringJobStore — DateTimeOffset comparison tests
    // =========================================================================

    [Fact]
    public async Task GetDueAsync_ReturnsDueJobs()
    {
        var store = Services.GetRequiredService<IRecurringJobStore>();
        var now = DateTimeOffset.UtcNow;

        var dueId = $"due-job-{Guid.NewGuid():N}";
        var futureId = $"future-job-{Guid.NewGuid():N}";

        await store.UpsertAsync(new RecurringJobDefinition
        {
            Id = dueId,
            JobType = "IntegrationTest.DueRecurring",
            CronExpression = "0 * * * *",
            IsEnabled = true,
            NextExecutionAt = now.AddMinutes(-1)
        });

        await store.UpsertAsync(new RecurringJobDefinition
        {
            Id = futureId,
            JobType = "IntegrationTest.FutureRecurring",
            CronExpression = "0 * * * *",
            IsEnabled = true,
            NextExecutionAt = now.AddMinutes(10)
        });

        var dueJobs = await store.GetDueAsync(now);

        dueJobs.Should().Contain(j => j.Id == dueId,
            "a recurring job with NextExecutionAt in the past should be due");
        dueJobs.Should().NotContain(j => j.Id == futureId,
            "a recurring job with NextExecutionAt in the future should NOT be due");
    }

    [Fact]
    public async Task GetDueAsync_ExcludesDisabledJobs()
    {
        var store = Services.GetRequiredService<IRecurringJobStore>();
        var now = DateTimeOffset.UtcNow;

        var jobId = $"disabled-job-{Guid.NewGuid():N}";

        await store.UpsertAsync(new RecurringJobDefinition
        {
            Id = jobId,
            JobType = "IntegrationTest.DisabledRecurring",
            CronExpression = "0 * * * *",
            IsEnabled = true,
            NextExecutionAt = now.AddMinutes(-1)
        });

        await store.DisableAsync(jobId);

        var dueJobs = await store.GetDueAsync(now);

        dueJobs.Should().NotContain(j => j.Id == jobId,
            "a disabled recurring job should be excluded from due results");
    }
}
