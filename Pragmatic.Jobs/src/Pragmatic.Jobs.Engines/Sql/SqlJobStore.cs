using Microsoft.Extensions.Logging;
using Pragmatic.Persistence.Sql;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Jobs.Engines.Sql;

/// <summary>
///     The <see cref="IJobStore" /> of the <c>[EnableJobPersistence]</c> boundary the Sql engine serves: what <c>EfCoreJobStore</c> does, on the
///     same table, through the boundary's session. Every transition is one conditional update, its guard in the statement: the lease is taken by
///     the one worker whose update matched the row, and the marks are fenced on the worker that holds it.
/// </summary>
public sealed partial class SqlJobStore(SqlSession session, IClock clock, ILogger<SqlJobStore> logger) : IJobStore
{
    private const int Pending = (int)JobStatus.Pending;
    private const int Running = (int)JobStatus.Running;
    private const int Completed = (int)JobStatus.Completed;
    private const int Failed = (int)JobStatus.Failed;
    private const int Cancelled = (int)JobStatus.Cancelled;

    /// <inheritdoc />
    public async Task<JobInstance> EnqueueAsync(JobInstance job, CancellationToken ct = default)
    {
        job.Id = job.Id == Guid.Empty ? Guid.NewGuid() : job.Id;
        job.CreatedAt = clock.UtcNow;
        job.Status = JobStatus.Pending;
        await session.InsertAsync(JobRecords.From(new JobRow(), job), ct).ConfigureAwait(false);
        return job;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobInstance>> GetPendingAsync(int batchSize, DateTimeOffset now, CancellationToken ct = default)
    {
        var rows = await session.Query<JobRow>().Where((r, a) => r.Status == a.Pending && r.ScheduledFor <= a.Now, (Pending, Now: now))
            .OrderByDescending(r => r.Priority).ThenBy(r => r.ScheduledFor).Take(batchSize).ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(JobRecords.ToInstance)];
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireLeaseAsync(Guid jobId, string workerId, TimeSpan leaseTime, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        // The status guard is what stops a terminal job being re-acquired: MarkCompletedAsync clears LeasedBy, so a predicate testing only the
        // lease fields would re-run a job that already finished. Acquirable means: waiting, or running under a lease that has expired.
        var affected = await session.Query<JobRow>()
            .Where((r, a) => r.Id == a.Id && (r.Status == a.Pending || (r.Status == a.Running && r.LeaseExpiresAt < a.Now)),
                (Id: jobId, Pending, Running, Now: now))
            .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.LeasedBy, a.Worker).SetProperty(r => r.LeaseExpiresAt, a.Until)
                    .SetProperty(r => r.Status, a.Running).SetProperty(r => r.StartedAt, a.Now),
                (Worker: (string?)workerId, Until: (DateTimeOffset?)now.Add(leaseTime), Running, Now: (DateTimeOffset?)now), ct).ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task RenewLeaseAsync(Guid jobId, string workerId, TimeSpan leaseTime, CancellationToken ct = default)
    {
        // On Running and the holder: a stale worker cannot extend a lease another worker has taken since.
        await session.Query<JobRow>().Where((r, a) => r.Id == a.Id && r.LeasedBy == a.Worker && r.Status == a.Running, (Id: jobId, Worker: workerId, Running))
            .ExecuteUpdateAsync((s, until) => s.SetProperty(r => r.LeaseExpiresAt, until), (DateTimeOffset?)clock.UtcNow.Add(leaseTime), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReleaseExpiredLeasesAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        // A crash counts as an attempt: a job that reliably kills its worker would otherwise return to Pending forever.
        var released = await session.Query<JobRow>()
            .Where((r, a) => r.Status == a.Running && r.LeaseExpiresAt < a.Now && r.Attempt + 1 < r.MaxAttempts, (Running, Now: now))
            .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.LeasedBy, a.Holder).SetProperty(r => r.LeaseExpiresAt, a.Until)
                    .SetProperty(r => r.Attempt, r => r.Attempt + 1).SetProperty(r => r.Status, a.Pending),
                (Holder: (string?)null, Until: (DateTimeOffset?)null, Pending), ct).ConfigureAwait(false);

        // Orphans that have exhausted their budget become terminal instead of looping.
        var exhausted = await session.Query<JobRow>()
            .Where((r, a) => r.Status == a.Running && r.LeaseExpiresAt < a.Now && r.Attempt + 1 >= r.MaxAttempts, (Running, Now: now))
            .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.LeasedBy, a.Holder).SetProperty(r => r.LeaseExpiresAt, a.Until)
                    .SetProperty(r => r.Attempt, r => r.Attempt + 1).SetProperty(r => r.Error, a.Error)
                    .SetProperty(r => r.CompletedAt, a.Now).SetProperty(r => r.Status, a.Failed),
                (Holder: (string?)null, Until: (DateTimeOffset?)null, Error: (string?)"Worker lease expired without completion", Now: (DateTimeOffset?)now, Failed), ct)
            .ConfigureAwait(false);

        if (exhausted > 0)
            LogOrphansFailed(exhausted);
        if (released > 0)
            LogLeasesReleased(released);
    }

    /// <inheritdoc />
    public async Task ReleaseLeaseAsync(Guid jobId, string workerId, CancellationToken ct = default)
    {
        // On the holder: a worker that already lost its lease cannot reset a job another worker is now running.
        await session.Query<JobRow>().Where((r, a) => r.Id == a.Id && r.LeasedBy == a.Worker && r.Status == a.Running, (Id: jobId, Worker: workerId, Running))
            .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.LeasedBy, a.Holder).SetProperty(r => r.LeaseExpiresAt, a.Until).SetProperty(r => r.Status, a.Pending),
                (Holder: (string?)null, Until: (DateTimeOffset?)null, Pending), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkCompletedAsync(Guid jobId, string workerId, CancellationToken ct = default)
    {
        // On the holder and on Running: a worker whose lease was taken over must not stamp the job complete, nor resurrect a cancelled one.
        await session.Query<JobRow>().Where((r, a) => r.Id == a.Id && r.LeasedBy == a.Worker && r.Status == a.Running, (Id: jobId, Worker: workerId, Running))
            .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.Status, a.Completed).SetProperty(r => r.CompletedAt, a.Now)
                    .SetProperty(r => r.LeasedBy, a.Holder).SetProperty(r => r.LeaseExpiresAt, a.Until),
                (Completed, Now: (DateTimeOffset?)clock.UtcNow, Holder: (string?)null, Until: (DateTimeOffset?)null), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(Guid jobId, string workerId, string error, int attempt, TimeSpan retryDelay, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var truncated = (string?)JobRecords.Truncate(error);

        // Two guarded statements: exactly one predicate can match. A retry is scheduled past the caller's backoff, so the next poll does not
        // select it again at once.
        var retried = await session.Query<JobRow>()
            .Where((r, a) => r.Id == a.Id && r.LeasedBy == a.Worker && r.Status == a.Running && a.Attempt < r.MaxAttempts,
                (Id: jobId, Worker: workerId, Running, Attempt: attempt))
            .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.Attempt, a.Attempt).SetProperty(r => r.Error, a.Error)
                    .SetProperty(r => r.LeasedBy, a.Holder).SetProperty(r => r.LeaseExpiresAt, a.Until)
                    .SetProperty(r => r.ScheduledFor, a.Next).SetProperty(r => r.Status, a.Pending),
                (Attempt: attempt, Error: truncated, Holder: (string?)null, Until: (DateTimeOffset?)null, Next: now + retryDelay, Pending), ct)
            .ConfigureAwait(false);
        if (retried > 0)
            return;

        await session.Query<JobRow>()
            .Where((r, a) => r.Id == a.Id && r.LeasedBy == a.Worker && r.Status == a.Running && a.Attempt >= r.MaxAttempts,
                (Id: jobId, Worker: workerId, Running, Attempt: attempt))
            .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.Attempt, a.Attempt).SetProperty(r => r.Error, a.Error)
                    .SetProperty(r => r.LeasedBy, a.Holder).SetProperty(r => r.LeaseExpiresAt, a.Until)
                    .SetProperty(r => r.CompletedAt, a.Now).SetProperty(r => r.Status, a.Failed),
                (Attempt: attempt, Error: truncated, Holder: (string?)null, Until: (DateTimeOffset?)null, Now: (DateTimeOffset?)now, Failed), ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkCancelledAsync(Guid jobId, CancellationToken ct = default)
    {
        // Only a job not yet terminal, and its lease cleared, so a worker holding a stale snapshot cannot acquire it afterwards.
        await session.Query<JobRow>()
            .Where((r, a) => r.Id == a.Id && r.Status != a.Completed && r.Status != a.Failed && r.Status != a.Cancelled, (Id: jobId, Completed, Failed, Cancelled))
            .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.Status, a.Cancelled).SetProperty(r => r.LeasedBy, a.Holder)
                    .SetProperty(r => r.LeaseExpiresAt, a.Until).SetProperty(r => r.CompletedAt, a.Now),
                (Cancelled, Holder: (string?)null, Until: (DateTimeOffset?)null, Now: (DateTimeOffset?)clock.UtcNow), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<JobInstance?> GetAsync(Guid jobId, CancellationToken ct = default)
    {
        var row = await session.Query<JobRow>().FirstOrDefaultAsync((r, id) => r.Id == id, jobId, ct).ConfigureAwait(false);
        return row is null ? null : JobRecords.ToInstance(row);
    }

    /// <inheritdoc />
    public async Task<int> PurgeTerminalAsync(DateTimeOffset olderThan, int batchSize, CancellationToken ct = default)
    {
        // The keys first, then the delete by key: a batch's Take is no part of a DELETE on every database. The terminal guard is checked
        // again in the delete, so nothing that left a terminal state in between is removed.
        var doomed = await session.Query<JobRow>()
            .Where((r, a) => r.CompletedAt != null && r.CompletedAt < a.Cutoff && (r.Status == a.Completed || r.Status == a.Failed || r.Status == a.Cancelled),
                (Cutoff: olderThan, Completed, Failed, Cancelled))
            .OrderBy(r => r.CompletedAt).Take(batchSize).Select(r => r.Id).ToListAsync(ct).ConfigureAwait(false);
        if (doomed.Count == 0)
            return 0;

        var deleted = await session.Query<JobRow>()
            .Where((r, a) => a.Ids.Contains(r.Id) && (r.Status == a.Completed || r.Status == a.Failed || r.Status == a.Cancelled),
                (Ids: doomed, Completed, Failed, Cancelled))
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        if (deleted > 0)
            LogJobsPurged(deleted, olderThan);
        return deleted;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Purged {Count} terminal job(s) completed before {Cutoff}")]
    private partial void LogJobsPurged(int count, DateTimeOffset cutoff);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Released {Count} expired job leases")]
    private partial void LogLeasesReleased(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed {Count} orphaned job(s) that exhausted their attempts without completing")]
    private partial void LogOrphansFailed(int count);
}
