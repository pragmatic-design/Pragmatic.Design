using System.Collections.Concurrent;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Jobs.Stores;

/// <summary>
///     In-memory job store for development and testing.
///     State is lost on process restart.
/// </summary>
/// <remarks>
///     Timestamps come from the injected <see cref="IClock"/> rather than <c>DateTimeOffset.UtcNow</c>
///     so tests can drive lease expiry and retry backoff deterministically, and so a job's
///     <c>CreatedAt</c> agrees with the <c>ScheduledFor</c> the scheduler derived from the same clock.
/// </remarks>
public sealed class InMemoryJobStore(IClock clock) : IJobStore
{
    private readonly ConcurrentDictionary<Guid, JobInstance> _jobs = new();
    private readonly Lock _leaseLock = new();

    /// <inheritdoc />
    public Task<JobInstance> EnqueueAsync(JobInstance job, CancellationToken ct = default)
    {
        job.Id = job.Id == Guid.Empty ? Guid.NewGuid() : job.Id;
        job.CreatedAt = clock.UtcNow;
        job.Status = JobStatus.Pending;
        _jobs[job.Id] = job;
        return Task.FromResult(job);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<JobInstance>> GetPendingAsync(int batchSize, DateTimeOffset now, CancellationToken ct = default)
    {
        IReadOnlyList<JobInstance> result = _jobs.Values
            .Where(j => j.Status == JobStatus.Pending && j.ScheduledFor <= now)
            .OrderByDescending(j => j.Priority)
            .ThenBy(j => j.ScheduledFor)
            .Take(batchSize)
            .ToList();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<bool> TryAcquireLeaseAsync(Guid jobId, string workerId, TimeSpan leaseTime, CancellationToken ct = default)
    {
        // Lock guarantees atomicity of check+acquire across concurrent workers.
        // Without it, two workers could both see "not leased" and both write,
        // resulting in duplicate execution of the same job.
        lock (_leaseLock)
        {
            if (!_jobs.TryGetValue(jobId, out var job))
                return Task.FromResult(false);

            var now = clock.UtcNow;

            // Acquirable means waiting, or running under an expired lease. Testing only the lease
            // fields would let a completed job — whose LeasedBy was cleared on completion — run again.
            var acquirable = job.Status == JobStatus.Pending
                || (job.Status == JobStatus.Running && job.LeaseExpiresAt < now);

            if (!acquirable)
                return Task.FromResult(false);

            job.LeasedBy = workerId;
            job.LeaseExpiresAt = now.Add(leaseTime);
            job.Status = JobStatus.Running;
            job.StartedAt = now;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task RenewLeaseAsync(Guid jobId, string workerId, TimeSpan leaseTime, CancellationToken ct = default)
    {
        // Same lock as TryAcquireLeaseAsync — renew, acquire and expiry must not interleave
        // on the same job's lease fields.
        lock (_leaseLock)
        {
            if (_jobs.TryGetValue(jobId, out var job)
                && job.LeasedBy == workerId
                && job.Status == JobStatus.Running)
            {
                job.LeaseExpiresAt = clock.UtcNow.Add(leaseTime);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReleaseExpiredLeasesAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        lock (_leaseLock)
        {
            foreach (var job in _jobs.Values)
            {
                if (job.Status != JobStatus.Running || job.LeaseExpiresAt >= now)
                    continue;

                job.LeasedBy = null;
                job.LeaseExpiresAt = null;

                // Count the crash as an attempt, otherwise a job that reliably kills its worker
                // returns to Pending forever and never reaches MaxAttempts.
                job.Attempt++;

                if (job.Attempt >= job.MaxAttempts)
                {
                    job.Status = JobStatus.Failed;
                    job.Error = "Worker lease expired without completion";
                    job.CompletedAt = now;
                }
                else
                {
                    job.Status = JobStatus.Pending;
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReleaseLeaseAsync(Guid jobId, string workerId, CancellationToken ct = default)
    {
        lock (_leaseLock)
        {
            // Guarded on the holder so a worker that already lost its lease cannot reset a job
            // another worker is now running.
            if (_jobs.TryGetValue(jobId, out var job)
                && job.LeasedBy == workerId
                && job.Status == JobStatus.Running)
            {
                job.LeasedBy = null;
                job.LeaseExpiresAt = null;
                job.Status = JobStatus.Pending;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkCompletedAsync(Guid jobId, string workerId, CancellationToken ct = default)
    {
        // Acquire the same lock as TryAcquireLeaseAsync to prevent torn state
        // between lease fields and status when concurrent workers race on the same job.
        lock (_leaseLock)
        {
            // Fenced on the holder and on Running: a worker whose lease was taken over must not
            // stamp the job complete, and a cancelled job must not be resurrected as Completed.
            if (_jobs.TryGetValue(jobId, out var job)
                && job.LeasedBy == workerId
                && job.Status == JobStatus.Running)
            {
                job.Status = JobStatus.Completed;
                job.CompletedAt = clock.UtcNow;
                job.LeasedBy = null;
                job.LeaseExpiresAt = null;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkFailedAsync(Guid jobId, string workerId, string error, int attempt, TimeSpan retryDelay, CancellationToken ct = default)
    {
        lock (_leaseLock)
        {
            if (_jobs.TryGetValue(jobId, out var job)
                && job.LeasedBy == workerId
                && job.Status == JobStatus.Running)
            {
                var now = clock.UtcNow;

                job.Attempt = attempt;
                job.Error = error;
                job.LeasedBy = null;
                job.LeaseExpiresAt = null;

                if (attempt >= job.MaxAttempts)
                {
                    job.Status = JobStatus.Failed;
                    job.CompletedAt = now;
                }
                else
                {
                    // Delay the retry, otherwise the job is re-selected on the very next poll and
                    // burns its whole attempt budget within a few seconds.
                    job.Status = JobStatus.Pending;
                    job.ScheduledFor = now + retryDelay;
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkCancelledAsync(Guid jobId, CancellationToken ct = default)
    {
        lock (_leaseLock)
        {
            // Only non-terminal jobs can be cancelled, and the lease is cleared so a worker holding
            // a stale snapshot cannot acquire it afterwards.
            if (_jobs.TryGetValue(jobId, out var job)
                && job.Status is not (JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled))
            {
                job.Status = JobStatus.Cancelled;
                job.CompletedAt = clock.UtcNow;
                job.LeasedBy = null;
                job.LeaseExpiresAt = null;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<JobInstance?> GetAsync(Guid jobId, CancellationToken ct = default)
    {
        _jobs.TryGetValue(jobId, out var job);
        return Task.FromResult(job);
    }

    /// <inheritdoc />
    public Task<int> PurgeTerminalAsync(DateTimeOffset olderThan, int batchSize, CancellationToken ct = default)
    {
        var doomed = _jobs.Values
            .Where(j => j.CompletedAt is not null
                && j.CompletedAt < olderThan
                && j.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
            .OrderBy(j => j.CompletedAt)
            .Take(batchSize)
            .Select(j => j.Id)
            .ToList();

        var deleted = doomed.Count(id => _jobs.TryRemove(id, out _));
        return Task.FromResult(deleted);
    }

    /// <summary>Total job count (for testing).</summary>
    public int Count => _jobs.Count;
}
