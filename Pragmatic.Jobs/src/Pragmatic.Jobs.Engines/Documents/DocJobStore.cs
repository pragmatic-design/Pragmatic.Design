using Microsoft.Extensions.Logging;
using Pragmatic.Persistence.Documents;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Jobs.Engines.Documents;

/// <summary>
///     The <see cref="IJobStore" /> of the <c>[EnableJobPersistence]</c> boundary the documents engine serves: what <c>EfCoreJobStore</c> does,
///     through the boundary's session. Every transition is one conditional update by filter, its guard in the filter: on MongoDB the update of
///     a document is atomic, so the lease goes to the one worker whose update matched it.
/// </summary>
/// <remarks>
///     On Cosmos DB, which updates a document by its id only, a conditional update is the engine's read-then-replace at the document's etag: a
///     document another worker leased in between fails the etag, is read again, and no longer matches.
/// </remarks>
public sealed partial class DocJobStore(DocSession session, IClock clock, ILogger<DocJobStore> logger) : IJobStore
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
        await session.InsertAsync(JobRecords.From(new JobDocument(), job), ct).ConfigureAwait(false);
        return job;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobInstance>> GetPendingAsync(int batchSize, DateTimeOffset now, CancellationToken ct = default)
    {
        var documents = await session.Query<JobDocument>().Where((d, a) => d.Status == a.Pending && d.ScheduledFor <= a.Now, (Pending, Now: now.UtcTicks))
            .OrderByDescending(d => d.Priority).ThenBy(d => d.ScheduledFor).Take(batchSize).ToListAsync(ct).ConfigureAwait(false);
        return [.. documents.Select(JobRecords.ToInstance)];
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireLeaseAsync(Guid jobId, string workerId, TimeSpan leaseTime, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        // The status guard is what stops a terminal job being re-acquired: acquirable means waiting, or running under a lease that has expired.
        var affected = await session.Query<JobDocument>()
            .Where((d, a) => d.Id == a.Id && (d.Status == a.Pending || (d.Status == a.Running && d.LeaseExpiresAt < a.Now)),
                (Id: jobId, Pending, Running, Now: now.UtcTicks))
            .ExecuteUpdateAsync((s, a) => s.Set(d => d.LeasedBy, a.Worker).Set(d => d.LeaseExpiresAt, a.Until).Set(d => d.Status, a.Running).Set(d => d.StartedAt, a.Now),
                (Worker: (string?)workerId, Until: (long?)now.Add(leaseTime).UtcTicks, Running, Now: (long?)now.UtcTicks), ct).ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task RenewLeaseAsync(Guid jobId, string workerId, TimeSpan leaseTime, CancellationToken ct = default)
    {
        // On Running and the holder: a stale worker cannot extend a lease another worker has taken since.
        await session.Query<JobDocument>().Where((d, a) => d.Id == a.Id && d.LeasedBy == a.Worker && d.Status == a.Running, (Id: jobId, Worker: workerId, Running))
            .ExecuteUpdateAsync((s, until) => s.Set(d => d.LeaseExpiresAt, until), (long?)clock.UtcNow.Add(leaseTime).UtcTicks, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     A crash counts as an attempt: a job that reliably kills its worker would otherwise return to Pending forever, and one that has
    ///     exhausted its budget becomes terminal. The documents engine compares a field with a value, not with another field, so the budget is
    ///     read from each expired job and the job moved by a compare-and-swap on what was read: still running, still expired, at the attempt
    ///     counted. Two sweepers racing the same orphan count it once; the second finds the attempt moved and matches nothing.
    /// </remarks>
    public async Task ReleaseExpiredLeasesAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var expired = await session.Query<JobDocument>().Where((d, a) => d.Status == a.Running && d.LeaseExpiresAt < a.Now, (Running, Now: now.UtcTicks))
            .ToListAsync(ct).ConfigureAwait(false);

        long released = 0, exhausted = 0;
        foreach (var orphan in expired)
        {
            var guard = (Id: orphan.Id, Running, Now: now.UtcTicks, Attempt: orphan.Attempt);
            if (orphan.Attempt + 1 < orphan.MaxAttempts)
            {
                released += await session.Query<JobDocument>()
                    .Where((d, a) => d.Id == a.Id && d.Status == a.Running && d.LeaseExpiresAt < a.Now && d.Attempt == a.Attempt, guard)
                    .ExecuteUpdateAsync((s, a) => s.Set(d => d.LeasedBy, a.Holder).Set(d => d.LeaseExpiresAt, a.Until).Set(d => d.Attempt, a.Attempt).Set(d => d.Status, a.Pending),
                        (Holder: (string?)null, Until: (long?)null, Attempt: orphan.Attempt + 1, Pending), ct).ConfigureAwait(false);
                continue;
            }

            exhausted += await session.Query<JobDocument>()
                .Where((d, a) => d.Id == a.Id && d.Status == a.Running && d.LeaseExpiresAt < a.Now && d.Attempt == a.Attempt, guard)
                .ExecuteUpdateAsync((s, a) => s.Set(d => d.LeasedBy, a.Holder).Set(d => d.LeaseExpiresAt, a.Until).Set(d => d.Attempt, a.Attempt)
                        .Set(d => d.Error, a.Error).Set(d => d.CompletedAt, a.Now).Set(d => d.Status, a.Failed),
                    (Holder: (string?)null, Until: (long?)null, Attempt: orphan.Attempt + 1, Error: (string?)"Worker lease expired without completion",
                        Now: (long?)now.UtcTicks, Failed), ct).ConfigureAwait(false);
        }

        if (exhausted > 0)
            LogOrphansFailed(exhausted);
        if (released > 0)
            LogLeasesReleased(released);
    }

    /// <inheritdoc />
    public async Task ReleaseLeaseAsync(Guid jobId, string workerId, CancellationToken ct = default)
    {
        // On the holder: a worker that already lost its lease cannot reset a job another worker is now running.
        await session.Query<JobDocument>().Where((d, a) => d.Id == a.Id && d.LeasedBy == a.Worker && d.Status == a.Running, (Id: jobId, Worker: workerId, Running))
            .ExecuteUpdateAsync((s, a) => s.Set(d => d.LeasedBy, a.Holder).Set(d => d.LeaseExpiresAt, a.Until).Set(d => d.Status, a.Pending),
                (Holder: (string?)null, Until: (long?)null, Pending), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkCompletedAsync(Guid jobId, string workerId, CancellationToken ct = default)
    {
        // On the holder and on Running: a worker whose lease was taken over must not stamp the job complete, nor resurrect a cancelled one.
        await session.Query<JobDocument>().Where((d, a) => d.Id == a.Id && d.LeasedBy == a.Worker && d.Status == a.Running, (Id: jobId, Worker: workerId, Running))
            .ExecuteUpdateAsync((s, a) => s.Set(d => d.Status, a.Completed).Set(d => d.CompletedAt, a.Now).Set(d => d.LeasedBy, a.Holder).Set(d => d.LeaseExpiresAt, a.Until),
                (Completed, Now: (long?)clock.UtcNow.UtcTicks, Holder: (string?)null, Until: (long?)null), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(Guid jobId, string workerId, string error, int attempt, TimeSpan retryDelay, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var truncated = (string?)JobRecords.Truncate(error);

        // Two guarded updates: exactly one filter can match. A retry is scheduled past the caller's backoff.
        var retried = await session.Query<JobDocument>()
            .Where((d, a) => d.Id == a.Id && d.LeasedBy == a.Worker && d.Status == a.Running && a.Attempt < d.MaxAttempts,
                (Id: jobId, Worker: workerId, Running, Attempt: attempt))
            .ExecuteUpdateAsync((s, a) => s.Set(d => d.Attempt, a.Attempt).Set(d => d.Error, a.Error).Set(d => d.LeasedBy, a.Holder)
                    .Set(d => d.LeaseExpiresAt, a.Until).Set(d => d.ScheduledFor, a.Next).Set(d => d.Status, a.Pending),
                (Attempt: attempt, Error: truncated, Holder: (string?)null, Until: (long?)null, Next: (now + retryDelay).UtcTicks, Pending), ct)
            .ConfigureAwait(false);
        if (retried > 0)
            return;

        await session.Query<JobDocument>()
            .Where((d, a) => d.Id == a.Id && d.LeasedBy == a.Worker && d.Status == a.Running && a.Attempt >= d.MaxAttempts,
                (Id: jobId, Worker: workerId, Running, Attempt: attempt))
            .ExecuteUpdateAsync((s, a) => s.Set(d => d.Attempt, a.Attempt).Set(d => d.Error, a.Error).Set(d => d.LeasedBy, a.Holder)
                    .Set(d => d.LeaseExpiresAt, a.Until).Set(d => d.CompletedAt, a.Now).Set(d => d.Status, a.Failed),
                (Attempt: attempt, Error: truncated, Holder: (string?)null, Until: (long?)null, Now: (long?)now.UtcTicks, Failed), ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkCancelledAsync(Guid jobId, CancellationToken ct = default)
    {
        // Only a job not yet terminal, and its lease cleared, so a worker holding a stale snapshot cannot acquire it afterwards.
        await session.Query<JobDocument>()
            .Where((d, a) => d.Id == a.Id && d.Status != a.Completed && d.Status != a.Failed && d.Status != a.Cancelled, (Id: jobId, Completed, Failed, Cancelled))
            .ExecuteUpdateAsync((s, a) => s.Set(d => d.Status, a.Cancelled).Set(d => d.LeasedBy, a.Holder).Set(d => d.LeaseExpiresAt, a.Until).Set(d => d.CompletedAt, a.Now),
                (Cancelled, Holder: (string?)null, Until: (long?)null, Now: (long?)clock.UtcNow.UtcTicks), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<JobInstance?> GetAsync(Guid jobId, CancellationToken ct = default)
    {
        var document = await session.Query<JobDocument>().FirstOrDefaultAsync((d, id) => d.Id == id, jobId, ct).ConfigureAwait(false);
        return document is null ? null : JobRecords.ToInstance(document);
    }

    /// <inheritdoc />
    public async Task<int> PurgeTerminalAsync(DateTimeOffset olderThan, int batchSize, CancellationToken ct = default)
    {
        // The keys first, then the delete by key: a delete by filter removes every document it selects, so the batch is chosen before it. The
        // terminal guard is checked again in the delete.
        var doomed = await session.Query<JobDocument>()
            .Where((d, a) => d.CompletedAt != null && d.CompletedAt < a.Cutoff && (d.Status == a.Completed || d.Status == a.Failed || d.Status == a.Cancelled),
                (Cutoff: olderThan.UtcTicks, Completed, Failed, Cancelled))
            .OrderBy(d => d.CompletedAt).Take(batchSize).Select(d => d.Id).ToListAsync(ct).ConfigureAwait(false);
        if (doomed.Count == 0)
            return 0;

        var deleted = (int)await session.Query<JobDocument>()
            .Where((d, a) => a.Ids.Contains(d.Id) && (d.Status == a.Completed || d.Status == a.Failed || d.Status == a.Cancelled),
                (Ids: doomed, Completed, Failed, Cancelled))
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        if (deleted > 0)
            LogJobsPurged(deleted, olderThan);
        return deleted;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Purged {Count} terminal job(s) completed before {Cutoff}")]
    private partial void LogJobsPurged(int count, DateTimeOffset cutoff);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Released {Count} expired job leases")]
    private partial void LogLeasesReleased(long count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed {Count} orphaned job(s) that exhausted their attempts without completing")]
    private partial void LogOrphansFailed(long count);
}
