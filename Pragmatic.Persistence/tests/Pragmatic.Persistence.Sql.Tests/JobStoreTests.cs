using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Engines.Sql;
using Pragmatic.Jobs.Extensions;
using Pragmatic.Persistence.Sql;
using Pragmatic.Temporal.Clock;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The durable job store on the Sql engine (Pragmatic.Jobs.Engines, #904), on five databases: its tables as the generated schema has them
///     (EF Core's <c>__Jobs</c> and <c>__RecurringJobs</c>, compared column for column by <c>TheSqlEnginesSchemaIsPragmaticsTests</c>), and the
///     contract of <c>EfCoreJobStore</c>: the lease, the marks fenced on its holder, the orphans' attempts, the purge, the recurring claim.
/// </summary>
[Collection("pragmatic databases")]
public class JobStoreTests(PragmaticDatabases databases)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    public static TheoryData<SqlDialect> Servers => [SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_job_is_polled_leased_once_and_marked_only_by_its_holder(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        var store = Store(session);

        var low = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-2)));
        var high = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1), priority: 5));
        var future = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddHours(1)));

        // Due only, the higher priority first, as many as asked.
        Assert.Equal([high.Id, low.Id], (await store.GetPendingAsync(10, DateTimeOffset.UtcNow)).Select(j => j.Id));
        Assert.Equal([high.Id], (await store.GetPendingAsync(1, DateTimeOffset.UtcNow)).Select(j => j.Id));
        Assert.DoesNotContain(future.Id, (await store.GetPendingAsync(10, DateTimeOffset.UtcNow)).Select(j => j.Id));

        Assert.True(await store.TryAcquireLeaseAsync(low.Id, "worker-a", Lease));
        Assert.False(await store.TryAcquireLeaseAsync(low.Id, "worker-b", Lease)); // a live lease is not taken
        var leased = await store.GetAsync(low.Id);
        Assert.NotNull(leased);
        Assert.Equal((JobStatus.Running, "worker-a"), (leased.Status, leased.LeasedBy));
        Assert.NotNull(leased.StartedAt);

        // Renewed and completed by the holder only.
        await store.RenewLeaseAsync(low.Id, "worker-b", TimeSpan.FromHours(2));
        Assert.Equal(leased.LeaseExpiresAt, (await store.GetAsync(low.Id))!.LeaseExpiresAt);
        await store.RenewLeaseAsync(low.Id, "worker-a", TimeSpan.FromHours(2));
        Assert.True((await store.GetAsync(low.Id))!.LeaseExpiresAt > leased.LeaseExpiresAt);
        await store.MarkCompletedAsync(low.Id, "worker-b");
        Assert.Equal(JobStatus.Running, (await store.GetAsync(low.Id))!.Status);
        await store.MarkCompletedAsync(low.Id, "worker-a");
        var completed = await store.GetAsync(low.Id);
        Assert.Equal((JobStatus.Completed, (string?)null, (DateTimeOffset?)null), (completed!.Status, completed.LeasedBy, completed.LeaseExpiresAt));
        Assert.NotNull(completed.CompletedAt);

        // A terminal job is never leased again; a cancelled one never runs.
        Assert.False(await store.TryAcquireLeaseAsync(low.Id, "worker-b", Lease));
        await store.MarkCancelledAsync(high.Id);
        Assert.False(await store.TryAcquireLeaseAsync(high.Id, "worker-a", Lease));
        await store.MarkCancelledAsync(low.Id); // terminal: left as it is
        Assert.Equal(JobStatus.Completed, (await store.GetAsync(low.Id))!.Status);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_failure_is_retried_after_its_backoff_until_the_attempts_are_spent(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        var store = Store(session);
        var job = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.True(await store.TryAcquireLeaseAsync(job.Id, "worker-a", Lease));
        await store.MarkFailedAsync(job.Id, "worker-b", "not mine", attempt: 1, TimeSpan.FromMinutes(5)); // fenced
        Assert.Equal(JobStatus.Running, (await store.GetAsync(job.Id))!.Status);

        await store.MarkFailedAsync(job.Id, "worker-a", new string('x', 5000), attempt: 1, TimeSpan.FromMinutes(5));
        var retried = await store.GetAsync(job.Id);
        Assert.Equal((JobStatus.Pending, 1, (string?)null), (retried!.Status, retried.Attempt, retried.LeasedBy));
        Assert.Equal(4096, retried.Error!.Length); // cut to the column, not refused by it
        Assert.True(retried.ScheduledFor > DateTimeOffset.UtcNow, "the retry waits out its backoff");
        Assert.Empty(await store.GetPendingAsync(10, DateTimeOffset.UtcNow));

        Assert.True(await store.TryAcquireLeaseAsync(job.Id, "worker-a", Lease));
        await store.MarkFailedAsync(job.Id, "worker-a", "boom", attempt: 3, TimeSpan.FromMinutes(5));
        var failed = await store.GetAsync(job.Id);
        Assert.Equal((JobStatus.Failed, 3, "boom"), (failed!.Status, failed.Attempt, failed.Error));
        Assert.NotNull(failed.CompletedAt);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task An_expired_lease_costs_an_attempt_and_a_spent_budget_fails_the_job(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        var store = Store(session);
        var orphan = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1), maxAttempts: 3));
        var poison = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1), maxAttempts: 1));
        var live = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1)));
        var shutdown = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1)));
        foreach (var crashed in new[] { orphan, poison })
            Assert.True(await store.TryAcquireLeaseAsync(crashed.Id, "crashed", TimeSpan.FromSeconds(1)));
        Assert.True(await store.TryAcquireLeaseAsync(live.Id, "alive", TimeSpan.FromHours(1)));
        Assert.True(await store.TryAcquireLeaseAsync(shutdown.Id, "leaving", Lease));

        await store.ReleaseExpiredLeasesAsync(DateTimeOffset.UtcNow.AddMinutes(1));

        var released = await store.GetAsync(orphan.Id);
        Assert.Equal((JobStatus.Pending, 1, (string?)null), (released!.Status, released.Attempt, released.LeasedBy));
        var spent = await store.GetAsync(poison.Id);
        Assert.Equal((JobStatus.Failed, 1), (spent!.Status, spent.Attempt));
        Assert.Equal("Worker lease expired without completion", spent.Error);
        Assert.Equal((JobStatus.Running, "alive"), ((await store.GetAsync(live.Id))!.Status, (await store.GetAsync(live.Id))!.LeasedBy));

        // A graceful shutdown hands the job back without counting an attempt.
        await store.ReleaseLeaseAsync(shutdown.Id, "leaving");
        var handedBack = await store.GetAsync(shutdown.Id);
        Assert.Equal((JobStatus.Pending, 0), (handedBack!.Status, handedBack.Attempt));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_purge_removes_old_terminal_jobs_only_a_batch_at_a_time(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        var store = Store(session);
        var finished = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var job = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1)));
            Assert.True(await store.TryAcquireLeaseAsync(job.Id, "worker", Lease));
            await store.MarkCompletedAsync(job.Id, "worker");
            finished.Add(job.Id);
        }

        var pending = await store.EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Equal(0, await store.PurgeTerminalAsync(DateTimeOffset.UtcNow.AddHours(-1), 100)); // nothing that old
        Assert.Equal(2, await store.PurgeTerminalAsync(DateTimeOffset.UtcNow.AddMinutes(1), 2));
        Assert.Equal(1, await store.PurgeTerminalAsync(DateTimeOffset.UtcNow.AddMinutes(1), 2));
        foreach (var id in finished)
            Assert.Null(await store.GetAsync(id));
        Assert.NotNull(await store.GetAsync(pending.Id)); // unfinished work is never purged
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_recurring_definition_keeps_its_schedule_across_upserts_and_a_tick_is_claimed_once(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        var store = new SqlRecurringJobStore(session);
        var due = DateTimeOffset.UtcNow.AddMinutes(-1);

        await store.UpsertAsync(Definition("nightly", due));
        await store.UpsertAsync(Definition("future", DateTimeOffset.UtcNow.AddHours(1)));
        await store.UpsertAsync(Definition("paused", due));
        await store.DisableAsync("paused");
        Assert.Equal(["nightly"], (await store.GetDueAsync(DateTimeOffset.UtcNow)).Select(d => d.Id));

        // A second upsert writes the declaration and keeps the scheduling state: a host restarting does not reschedule.
        var stored = await store.GetAsync("nightly");
        var redeclaration = Definition("nightly", DateTimeOffset.UtcNow.AddDays(3));
        redeclaration.CronExpression = "0 3 * * *";
        await store.UpsertAsync(redeclaration);
        await store.UpsertAsync(Definition("paused", due));
        var redeclared = await store.GetAsync("nightly");
        Assert.Equal(("0 3 * * *", stored!.NextExecutionAt), (redeclared!.CronExpression, redeclared.NextExecutionAt));
        Assert.False((await store.GetAsync("paused"))!.IsEnabled);

        // The compare-and-swap: the host that read the occurrence advances it, a second with the same reading does not.
        var next = due.AddDays(1);
        Assert.True(await store.TryClaimDueAsync("nightly", stored.NextExecutionAt, next, DateTimeOffset.UtcNow));
        Assert.False(await store.TryClaimDueAsync("nightly", stored.NextExecutionAt, next, DateTimeOffset.UtcNow));
        var claimed = await store.GetAsync("nightly");
        Assert.NotNull(claimed!.LastExecutedAt);
        Assert.Empty(await store.GetDueAsync(DateTimeOffset.UtcNow));

        // An occurrence never computed is claimed by the null it holds.
        await store.UpdateNextExecutionAsync("future", null, DateTimeOffset.UtcNow);
        Assert.True(await store.TryClaimDueAsync("future", null, next, DateTimeOffset.UtcNow));
        Assert.False(await store.TryClaimDueAsync("future", null, next, DateTimeOffset.UtcNow));

        await store.EnableAsync("paused");
        Assert.True((await store.GetAsync("paused"))!.IsEnabled);
        await store.DeleteAsync("paused");
        Assert.Null(await store.GetAsync("paused"));
    }

    /// <summary>The lease's atomicity, which only a database proves: sixteen workers on sixteen connections, one wins.</summary>
    [Theory]
    [MemberData(nameof(Servers))]
    public async Task Of_workers_racing_one_job_exactly_one_takes_the_lease(SqlDialect dialect)
    {
        using var setup = await databases.SessionAsync(dialect, null);
        var job = await Store(setup).EnqueueAsync(Job(DateTimeOffset.UtcNow.AddMinutes(-1)));

        var won = await Race(dialect, 16, (session, i) => Store(session).TryAcquireLeaseAsync(job.Id, $"worker-{i}", Lease));

        Assert.Equal(1, won);
    }

    /// <summary>The recurring claim's atomicity: eight hosts racing one tick, one enqueues it.</summary>
    [Theory]
    [MemberData(nameof(Servers))]
    public async Task Of_hosts_racing_one_tick_exactly_one_claims_it(SqlDialect dialect)
    {
        using var setup = await databases.SessionAsync(dialect, null);
        await new SqlRecurringJobStore(setup).UpsertAsync(Definition("hourly", DateTimeOffset.UtcNow.AddMinutes(-1)));
        var occurrence = (await new SqlRecurringJobStore(setup).GetAsync("hourly"))!.NextExecutionAt;

        var won = await Race(dialect, 8, (session, _) => new SqlRecurringJobStore(session).TryClaimDueAsync("hourly", occurrence, occurrence!.Value.AddHours(1), DateTimeOffset.UtcNow));

        Assert.Equal(1, won);
    }

    /// <summary>The host's switch: the store over the session the boundary's generated registration hands it, in place of the in-memory one.</summary>
    [Fact]
    public async Task UseSqlPersistence_runs_the_jobs_on_the_session_of_the_boundary_that_holds_them()
    {
        using var session = await databases.SessionAsync(SqlDialect.Sqlite, null);
        await using var provider = Host(services => services.AddKeyedSingleton<SqlSession>(typeof(JobStoreTests), session).AddSqlJobStore<JobStoreTests>());
        using var scope = provider.CreateScope();

        Assert.IsType<SqlJobStore>(scope.ServiceProvider.GetRequiredService<IJobStore>());
        Assert.IsType<SqlRecurringJobStore>(scope.ServiceProvider.GetRequiredService<IRecurringJobStore>());
    }

    /// <summary>The control: with no boundary marked [EnableJobPersistence] there is no session to run on, and the store says which mark is missing.</summary>
    [Fact]
    public async Task UseSqlPersistence_without_the_boundary_names_the_attribute()
    {
        await using var provider = Host(static _ => { });
        using var scope = provider.CreateScope();

        var refused = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IJobStore>());
        Assert.Contains("[EnableJobPersistence]", refused.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Host(Action<IServiceCollection> boundary)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        boundary(services);
        services.AddPragmaticJobs(jobs => jobs.UseSqlPersistence());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>
    ///     <paramref name="contenders" /> calls of <paramref name="contend" />, each on its own connection, released together; how many returned true.
    ///     A dedicated thread per contender: the barrier blocks every one of them, which on pool threads starves the pool.
    /// </summary>
    private async Task<int> Race(SqlDialect dialect, int contenders, Func<SqlSession, int, Task<bool>> contend)
    {
        var sessions = Enumerable.Range(0, contenders).Select(_ => databases.AnotherSession(dialect)).ToList();
        try
        {
            using var barrier = new Barrier(contenders);
            var results = await Task.WhenAll(sessions.Select((session, i) => Task.Factory.StartNew(() =>
            {
                barrier.SignalAndWait();
                return contend(session, i);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap()));
            return results.Count(won => won);
        }
        finally
        {
            foreach (var session in sessions)
                session.Dispose();
        }
    }

    private static SqlJobStore Store(SqlSession session) => new(session, SystemClock.Instance, NullLogger<SqlJobStore>.Instance);

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
