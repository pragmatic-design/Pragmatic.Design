using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Jobs.EFCore;

/// <summary>
///     EF Core-backed job store with lease-based distributed locking.
///     Uses atomic <c>ExecuteUpdateAsync</c> for lease acquisition.
/// </summary>
public sealed partial class EfCoreJobStore(
    DbContext dbContext,
    IClock clock,
    ILogger<EfCoreJobStore> logger) : IJobStore
{
    /// <inheritdoc />
    public async Task<JobInstance> EnqueueAsync(JobInstance job, CancellationToken ct = default)
    {
        job.Id = job.Id == Guid.Empty ? Guid.NewGuid() : job.Id;
        job.CreatedAt = clock.UtcNow;
        job.Status = JobStatus.Pending;

        dbContext.Set<JobInstance>().Add(job);
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return job;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobInstance>> GetPendingAsync(int batchSize, DateTimeOffset now, CancellationToken ct = default)
    {
        return await dbContext.Set<JobInstance>()
            .Where(j => j.Status == JobStatus.Pending && j.ScheduledFor <= now)
            .OrderByDescending(j => j.Priority)
            .ThenBy(j => j.ScheduledFor)
            .Take(batchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireLeaseAsync(Guid jobId, string workerId, TimeSpan leaseTime, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        // The status guard is what stops a *terminal* job being re-acquired: MarkCompletedAsync
        // clears LeasedBy, so a predicate testing only the lease fields happily re-runs a job that
        // already finished. Acquirable means: waiting, or running under a lease that has expired.
        var affected = await dbContext.Set<JobInstance>()
            .Where(j => j.Id == jobId
                && (j.Status == JobStatus.Pending
                    || (j.Status == JobStatus.Running && j.LeaseExpiresAt < now)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.LeasedBy, workerId)
                .SetProperty(j => j.LeaseExpiresAt, now.Add(leaseTime))
                .SetProperty(j => j.Status, JobStatus.Running)
                .SetProperty(j => j.StartedAt, now), ct)
            .ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task RenewLeaseAsync(Guid jobId, string workerId, TimeSpan leaseTime, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        // Filter on Status==Running prevents a stale worker from silently extending
        // a lease that has already been re-acquired by another worker.
        await dbContext.Set<JobInstance>()
            .Where(j => j.Id == jobId && j.LeasedBy == workerId && j.Status == JobStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.LeaseExpiresAt, now.Add(leaseTime)), ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReleaseExpiredLeasesAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        // Count the crash as an attempt. Without this a job that reliably kills its worker (OOM,
        // process kill) returns to Pending forever and never reaches MaxAttempts — MaxAttempts
        // would bound only exception failures, never crash failures.
        var released = await dbContext.Set<JobInstance>()
            .Where(j => j.Status == JobStatus.Running && j.LeaseExpiresAt < now && j.Attempt + 1 < j.MaxAttempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.LeasedBy, (string?)null)
                .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(j => j.Attempt, j => j.Attempt + 1)
                .SetProperty(j => j.Status, JobStatus.Pending), ct)
            .ConfigureAwait(false);

        // Orphans that have exhausted their budget become terminal instead of looping.
        var exhausted = await dbContext.Set<JobInstance>()
            .Where(j => j.Status == JobStatus.Running && j.LeaseExpiresAt < now && j.Attempt + 1 >= j.MaxAttempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.LeasedBy, (string?)null)
                .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(j => j.Attempt, j => j.Attempt + 1)
                .SetProperty(j => j.Error, "Worker lease expired without completion")
                .SetProperty(j => j.CompletedAt, now)
                .SetProperty(j => j.Status, JobStatus.Failed), ct)
            .ConfigureAwait(false);

        if (exhausted > 0)
            LogOrphansFailed(exhausted);

        if (released > 0)
            LogLeasesReleased(released);
    }

    /// <inheritdoc />
    public async Task ReleaseLeaseAsync(Guid jobId, string workerId, CancellationToken ct = default)
    {
        // Guarded on the holder so a worker that already lost its lease cannot reset a job
        // another worker is now running.
        await dbContext.Set<JobInstance>()
            .Where(j => j.Id == jobId && j.LeasedBy == workerId && j.Status == JobStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.LeasedBy, (string?)null)
                .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(j => j.Status, JobStatus.Pending), ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkCompletedAsync(Guid jobId, string workerId, CancellationToken ct = default)
    {
        // Fenced on the lease holder and on Running: a worker whose lease expired and was taken
        // over must not stamp the job complete, and a cancelled job must not be resurrected.
        await dbContext.Set<JobInstance>()
            .Where(j => j.Id == jobId && j.LeasedBy == workerId && j.Status == JobStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Completed)
                .SetProperty(j => j.CompletedAt, clock.UtcNow)
                .SetProperty(j => j.LeasedBy, (string?)null)
                .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null), ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(Guid jobId, string workerId, string error, int attempt, TimeSpan retryDelay, CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        // Advance ScheduledFor by the caller's backoff so a re-Pending job is not re-selected
        // on the very next poll (the only prior backoff was an in-process Task.Delay, lost on crash).
        var nextScheduledFor = now + retryDelay;
        var truncatedError = Truncate(error, MaxErrorLength);

        // Two guarded statements rather than one with CASE expressions: exactly one predicate can
        // match, each SetProperty is a plain assignment, and nothing depends on how a provider
        // translates a conditional over a captured local.
        var retried = await dbContext.Set<JobInstance>()
            .Where(j => j.Id == jobId && j.LeasedBy == workerId && j.Status == JobStatus.Running
                && attempt < j.MaxAttempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Attempt, attempt)
                .SetProperty(j => j.Error, truncatedError)
                .SetProperty(j => j.LeasedBy, (string?)null)
                .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(j => j.ScheduledFor, nextScheduledFor)
                .SetProperty(j => j.Status, JobStatus.Pending), ct)
            .ConfigureAwait(false);

        if (retried > 0)
            return;

        await dbContext.Set<JobInstance>()
            .Where(j => j.Id == jobId && j.LeasedBy == workerId && j.Status == JobStatus.Running
                && attempt >= j.MaxAttempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Attempt, attempt)
                .SetProperty(j => j.Error, truncatedError)
                .SetProperty(j => j.LeasedBy, (string?)null)
                .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(j => j.CompletedAt, now)
                .SetProperty(j => j.Status, JobStatus.Failed), ct)
            .ConfigureAwait(false);
    }

    // Matches the Error column width. An over-long exception message would otherwise throw inside
    // the failure handler on PostgreSQL, losing the retry state the handler was trying to record.
    private const int MaxErrorLength = 4096;

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value.Substring(0, maxLength);

    /// <inheritdoc />
    public async Task MarkCancelledAsync(Guid jobId, CancellationToken ct = default)
    {
        // Only non-terminal jobs can be cancelled, and the lease is cleared so a worker holding a
        // stale snapshot cannot acquire it afterwards — otherwise "cancel" left the job runnable.
        await dbContext.Set<JobInstance>()
            .Where(j => j.Id == jobId
                && j.Status != JobStatus.Completed
                && j.Status != JobStatus.Failed
                && j.Status != JobStatus.Cancelled)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Cancelled)
                .SetProperty(j => j.LeasedBy, (string?)null)
                .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(j => j.CompletedAt, clock.UtcNow), ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<JobInstance?> GetAsync(Guid jobId, CancellationToken ct = default)
    {
        return await dbContext.Set<JobInstance>()
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> PurgeTerminalAsync(DateTimeOffset olderThan, int batchSize, CancellationToken ct = default)
    {
        // Batched so a long-neglected table is drained over several passes instead of one
        // statement holding locks over millions of rows.
        var doomed = dbContext.Set<JobInstance>()
            .Where(j => j.CompletedAt != null
                && j.CompletedAt < olderThan
                && (j.Status == JobStatus.Completed
                    || j.Status == JobStatus.Failed
                    || j.Status == JobStatus.Cancelled))
            .OrderBy(j => j.CompletedAt)
            .Take(batchSize);

        var deleted = await doomed.ExecuteDeleteAsync(ct).ConfigureAwait(false);

        if (deleted > 0)
            LogJobsPurged(deleted, olderThan);

        return deleted;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Purged {Count} terminal job(s) completed before {Cutoff}")]
    partial void LogJobsPurged(int count, DateTimeOffset cutoff);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Released {Count} expired job leases")]
    partial void LogLeasesReleased(int count);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Failed {Count} orphaned job(s) that exhausted their attempts without completing")]
    partial void LogOrphansFailed(int count);
}
