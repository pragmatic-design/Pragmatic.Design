using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Engines.Documents;
using Pragmatic.Jobs.Extensions;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Schema;
using Pragmatic.Temporal.Clock;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     The durable job store on the documents engine (Pragmatic.Jobs.Engines, #904), on MongoDB and Cosmos DB: its collections created from the
///     schema the package declares (EF Core's indexes, by name), and the contract of <c>EfCoreJobStore</c>: the lease, the marks fenced on its
///     holder, the orphans' attempts, the purge, the recurring claim. The instants read back to the tick: the documents keep them as UTC ticks.
/// </summary>
[Collection("document servers")]
public class JobStoreTests(DocumentServers servers)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_job_is_polled_leased_once_and_marked_only_by_its_holder(Store server)
    {
        var session = await SessionAsync(server);
        var store = Jobs(session);

        var low = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-2)));
        var high = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1), priority: 5));
        var future = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddHours(1)));

        Assert.Equal([high.Id, low.Id], (await store.GetPendingAsync(10, DateTimeOffset.UtcNow)).Select(j => j.Id));
        Assert.Equal([high.Id], (await store.GetPendingAsync(1, DateTimeOffset.UtcNow)).Select(j => j.Id));
        Assert.DoesNotContain(future.Id, (await store.GetPendingAsync(10, DateTimeOffset.UtcNow)).Select(j => j.Id));
        Assert.Equal(low.ScheduledFor, (await store.GetAsync(low.Id))!.ScheduledFor); // to the tick

        Assert.True(await store.TryAcquireLeaseAsync(low.Id, "worker-a", Lease));
        Assert.False(await store.TryAcquireLeaseAsync(low.Id, "worker-b", Lease));
        var leased = await store.GetAsync(low.Id);
        Assert.Equal((JobStatus.Running, "worker-a"), (leased!.Status, leased.LeasedBy));

        await store.RenewLeaseAsync(low.Id, "worker-b", TimeSpan.FromHours(2));
        Assert.Equal(leased.LeaseExpiresAt, (await store.GetAsync(low.Id))!.LeaseExpiresAt);
        await store.RenewLeaseAsync(low.Id, "worker-a", TimeSpan.FromHours(2));
        Assert.True((await store.GetAsync(low.Id))!.LeaseExpiresAt > leased.LeaseExpiresAt);
        await store.MarkCompletedAsync(low.Id, "worker-b");
        Assert.Equal(JobStatus.Running, (await store.GetAsync(low.Id))!.Status);
        await store.MarkCompletedAsync(low.Id, "worker-a");
        var completed = await store.GetAsync(low.Id);
        Assert.Equal((JobStatus.Completed, (string?)null, (DateTimeOffset?)null), (completed!.Status, completed.LeasedBy, completed.LeaseExpiresAt));

        Assert.False(await store.TryAcquireLeaseAsync(low.Id, "worker-b", Lease));
        await store.MarkCancelledAsync(high.Id);
        Assert.False(await store.TryAcquireLeaseAsync(high.Id, "worker-a", Lease));
        await store.MarkCancelledAsync(low.Id);
        Assert.Equal(JobStatus.Completed, (await store.GetAsync(low.Id))!.Status);
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_failure_is_retried_after_its_backoff_until_the_attempts_are_spent(Store server)
    {
        var session = await SessionAsync(server);
        var store = Jobs(session);
        var job = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.True(await store.TryAcquireLeaseAsync(job.Id, "worker-a", Lease));
        await store.MarkFailedAsync(job.Id, "worker-b", "not mine", attempt: 1, TimeSpan.FromMinutes(5));
        Assert.Equal(JobStatus.Running, (await store.GetAsync(job.Id))!.Status);

        await store.MarkFailedAsync(job.Id, "worker-a", new string('x', 5000), attempt: 1, TimeSpan.FromMinutes(5));
        var retried = await store.GetAsync(job.Id);
        Assert.Equal((JobStatus.Pending, 1, 4096), (retried!.Status, retried.Attempt, retried.Error!.Length));
        Assert.True(retried.ScheduledFor > DateTimeOffset.UtcNow, "the retry waits out its backoff");
        Assert.Empty(await store.GetPendingAsync(10, DateTimeOffset.UtcNow));

        Assert.True(await store.TryAcquireLeaseAsync(job.Id, "worker-a", Lease));
        await store.MarkFailedAsync(job.Id, "worker-a", "boom", attempt: 3, TimeSpan.FromMinutes(5));
        var failed = await store.GetAsync(job.Id);
        Assert.Equal((JobStatus.Failed, 3, "boom"), (failed!.Status, failed.Attempt, failed.Error));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task An_expired_lease_costs_an_attempt_and_a_spent_budget_fails_the_job(Store server)
    {
        var session = await SessionAsync(server);
        var store = Jobs(session);
        var orphan = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1), maxAttempts: 3));
        var poison = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1), maxAttempts: 1));
        var live = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1)));
        var shutdown = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1)));
        foreach (var crashed in new[] { orphan, poison })
            Assert.True(await store.TryAcquireLeaseAsync(crashed.Id, "crashed", TimeSpan.FromSeconds(1)));
        Assert.True(await store.TryAcquireLeaseAsync(live.Id, "alive", TimeSpan.FromHours(1)));
        Assert.True(await store.TryAcquireLeaseAsync(shutdown.Id, "leaving", Lease));

        await store.ReleaseExpiredLeasesAsync(DateTimeOffset.UtcNow.AddMinutes(1));
        // A second sweep over the same instant finds nothing more to count: the attempt is taken once.
        await store.ReleaseExpiredLeasesAsync(DateTimeOffset.UtcNow.AddMinutes(1));

        var released = await store.GetAsync(orphan.Id);
        Assert.Equal((JobStatus.Pending, 1, (string?)null), (released!.Status, released.Attempt, released.LeasedBy));
        var spent = await store.GetAsync(poison.Id);
        Assert.Equal((JobStatus.Failed, 1, "Worker lease expired without completion"), (spent!.Status, spent.Attempt, spent.Error));
        Assert.Equal(JobStatus.Running, (await store.GetAsync(live.Id))!.Status);

        await store.ReleaseLeaseAsync(shutdown.Id, "leaving");
        var handedBack = await store.GetAsync(shutdown.Id);
        Assert.Equal((JobStatus.Pending, 0), (handedBack!.Status, handedBack.Attempt));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_purge_removes_old_terminal_jobs_only_a_batch_at_a_time(Store server)
    {
        var session = await SessionAsync(server);
        var store = Jobs(session);
        var finished = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var job = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1)));
            Assert.True(await store.TryAcquireLeaseAsync(job.Id, "worker", Lease));
            await store.MarkCompletedAsync(job.Id, "worker");
            finished.Add(job.Id);
        }

        var pending = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Equal(0, await store.PurgeTerminalAsync(DateTimeOffset.UtcNow.AddHours(-1), 100));
        Assert.Equal(2, await store.PurgeTerminalAsync(DateTimeOffset.UtcNow.AddMinutes(1), 2));
        Assert.Equal(1, await store.PurgeTerminalAsync(DateTimeOffset.UtcNow.AddMinutes(1), 2));
        foreach (var id in finished)
            Assert.Null(await store.GetAsync(id));
        Assert.NotNull(await store.GetAsync(pending.Id));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_recurring_definition_keeps_its_schedule_across_upserts_and_a_tick_is_claimed_once(Store server)
    {
        var session = await SessionAsync(server);
        var store = new DocRecurringJobStore(session);
        var due = DateTimeOffset.UtcNow.AddMinutes(-1);

        await store.UpsertAsync(Definition("nightly", due));
        await store.UpsertAsync(Definition("future", DateTimeOffset.UtcNow.AddHours(1)));
        await store.UpsertAsync(Definition("paused", due));
        await store.DisableAsync("paused");
        Assert.Equal(["nightly"], (await store.GetDueAsync(DateTimeOffset.UtcNow)).Select(d => d.Id));

        var stored = await store.GetAsync("nightly");
        Assert.Equal(due, stored!.NextExecutionAt); // to the tick, which the claim compares
        var redeclaration = Definition("nightly", DateTimeOffset.UtcNow.AddDays(3));
        redeclaration.CronExpression = "0 3 * * *";
        await store.UpsertAsync(redeclaration);
        await store.UpsertAsync(Definition("paused", due));
        var redeclared = await store.GetAsync("nightly");
        Assert.Equal(("0 3 * * *", stored.NextExecutionAt), (redeclared!.CronExpression, redeclared.NextExecutionAt));
        Assert.False((await store.GetAsync("paused"))!.IsEnabled);

        var next = due.AddDays(1);
        Assert.True(await store.TryClaimDueAsync("nightly", stored.NextExecutionAt, next, DateTimeOffset.UtcNow));
        Assert.False(await store.TryClaimDueAsync("nightly", stored.NextExecutionAt, next, DateTimeOffset.UtcNow));
        Assert.Empty(await store.GetDueAsync(DateTimeOffset.UtcNow));

        await store.UpdateNextExecutionAsync("future", null, DateTimeOffset.UtcNow);
        Assert.True(await store.TryClaimDueAsync("future", null, next, DateTimeOffset.UtcNow));
        Assert.False(await store.TryClaimDueAsync("future", null, next, DateTimeOffset.UtcNow));

        await store.EnableAsync("paused");
        Assert.True((await store.GetAsync("paused"))!.IsEnabled);
        await store.DeleteAsync("paused");
        Assert.Null(await store.GetAsync("paused"));
    }

    /// <summary>The lease's atomicity on the store itself: sixteen workers, each on its own session, one wins.</summary>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task Of_workers_racing_one_job_exactly_one_takes_the_lease(Store server)
    {
        var session = await SessionAsync(server);
        var job = await Jobs(session).EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1)));

        var won = await Race(session, 16, (contender, i) => Jobs(contender).TryAcquireLeaseAsync(job.Id, $"worker-{i}", Lease));

        Assert.Equal(1, won);
    }

    /// <summary>The recurring claim's atomicity: eight hosts racing one tick, one enqueues it.</summary>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task Of_hosts_racing_one_tick_exactly_one_claims_it(Store server)
    {
        var session = await SessionAsync(server);
        await new DocRecurringJobStore(session).UpsertAsync(Definition("hourly", DateTimeOffset.UtcNow.AddMinutes(-1)));
        var occurrence = (await new DocRecurringJobStore(session).GetAsync("hourly"))!.NextExecutionAt;

        var won = await Race(session, 8, (contender, _) => new DocRecurringJobStore(contender).TryClaimDueAsync("hourly", occurrence, occurrence!.Value.AddHours(1), DateTimeOffset.UtcNow));

        Assert.Equal(1, won);
    }

    /// <summary>The host's switch: the store over the session the boundary's generated registration hands it; without one it names the attribute.</summary>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task UseDocumentsPersistence_runs_the_jobs_on_the_session_of_the_boundary_that_holds_them(Store server)
    {
        var session = servers.Session(server);
        await using (var provider = Host(services => services.AddKeyedSingleton<DocSession>(typeof(JobStoreTests), session).AddDocJobStore<JobStoreTests>()))
        {
            using var scope = provider.CreateScope();
            Assert.IsType<DocJobStore>(scope.ServiceProvider.GetRequiredService<IJobStore>());
            Assert.IsType<DocRecurringJobStore>(scope.ServiceProvider.GetRequiredService<IRecurringJobStore>());
        }

        await using (var unmarked = Host(static _ => { }))
        {
            using var scope = unmarked.CreateScope();
            var refused = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IJobStore>());
            Assert.Contains("[EnableJobPersistence]", refused.Message, StringComparison.Ordinal);
        }
    }

    private async Task<DocSession> SessionAsync(Store server)
    {
        var session = servers.Session(server);
        var schema = await DocSchema.EnsureAsync(session, [JobDocument.Schema, RecurringJobDocument.Schema]);
        // MongoDB creates the indexes by name; Cosmos DB indexes every field of a container, so what it creates is the container.
        var created = server == Store.Mongo ? "IX___Jobs_Status_Priority_ScheduledFor" : "container";
        Assert.Contains(schema.Changes, c => c.Collection == "__Jobs" && c.Element == created && c.Outcome == DocSchemaOutcome.Created);
        return session;
    }

    private static ServiceProvider Host(Action<IServiceCollection> boundary)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        boundary(services);
        services.AddPragmaticJobs(jobs => jobs.UseDocumentsPersistence());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>
    ///     <paramref name="contenders" /> calls of <paramref name="contend" />, each on a session of its own over <paramref name="session" />'s
    ///     database, released together; how many returned true. A dedicated thread per contender, so the barrier does not starve the pool.
    /// </summary>
    private static async Task<int> Race(DocSession session, int contenders, Func<DocSession, int, Task<bool>> contend)
    {
        var sessions = Enumerable.Range(0, contenders).Select(_ => DocumentServers.As(session, null)).ToList();
        using var barrier = new Barrier(contenders);
        var results = await Task.WhenAll(sessions.Select((contender, i) => Task.Factory.StartNew(() =>
        {
            barrier.SignalAndWait();
            return contend(contender, i);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap()));
        return results.Count(won => won);
    }

    private static DocJobStore Jobs(DocSession session) => new(session, SystemClock.Instance, NullLogger<DocJobStore>.Instance);

    private static JobInstance Job(DateTimeOffset scheduledFor, int priority = 0, int maxAttempts = 3) => new()
    {
        JobType = "Probe.RosterJob",
        ScheduledFor = scheduledFor,
        Priority = priority,
        MaxAttempts = maxAttempts,
        ParametersJson = "{\"shift\":\"night\"}",
        CorrelationId = "roster",
    };

    private static RecurringJobDefinition Definition(string id, DateTimeOffset next) => new()
    {
        Id = id,
        JobType = "Probe.RosterJob",
        CronExpression = "0 * * * *",
        NextExecutionAt = next,
        IsEnabled = true,
    };
}
