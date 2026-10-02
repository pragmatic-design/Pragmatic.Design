namespace Pragmatic.Jobs;

/// <summary>
///     Persistence for job instances. The runtime processor uses this
///     to poll, acquire, and update job state.
/// </summary>
public interface IJobStore
{
    /// <summary>Enqueues a new job instance.</summary>
    Task<JobInstance> EnqueueAsync(JobInstance job, CancellationToken ct = default);

    /// <summary>Gets pending jobs that are due for execution (ScheduledFor &lt;= now).</summary>
    Task<IReadOnlyList<JobInstance>> GetPendingAsync(int batchSize, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Atomically acquires a lease on a job. Returns true if acquired.</summary>
    Task<bool> TryAcquireLeaseAsync(Guid jobId, string workerId, TimeSpan leaseTime, CancellationToken ct = default);

    /// <summary>Renews an existing lease for a long-running job.</summary>
    Task RenewLeaseAsync(Guid jobId, string workerId, TimeSpan leaseTime, CancellationToken ct = default);

    /// <summary>Releases expired leases so other workers can pick up jobs.</summary>
    Task ReleaseExpiredLeasesAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    ///     Releases the lease held by <paramref name="workerId"/> and returns the job to
    ///     <see cref="JobStatus.Pending"/> without counting an attempt. Used on graceful shutdown,
    ///     where the job was abandoned rather than failed, so it can be retried immediately instead
    ///     of waiting out the lease.
    /// </summary>
    Task ReleaseLeaseAsync(Guid jobId, string workerId, CancellationToken ct = default);

    /// <summary>
    ///     Marks a job as completed. Fenced on <paramref name="workerId"/>: a worker whose lease has
    ///     expired and been taken over cannot stamp the job complete underneath its new owner.
    /// </summary>
    Task MarkCompletedAsync(Guid jobId, string workerId, CancellationToken ct = default);

    /// <summary>
    ///     Marks a job as failed. If <paramref name="attempt"/> is below the job's MaxAttempts the
    ///     job returns to <see cref="JobStatus.Pending"/> with <paramref name="retryDelay"/> applied
    ///     to ScheduledFor; otherwise it becomes terminal. Fenced on <paramref name="workerId"/>.
    ///     The delay is supplied by the caller because it derives from the job's declared
    ///     <c>[Retry]</c> policy, which the store knows nothing about.
    /// </summary>
    Task MarkFailedAsync(Guid jobId, string workerId, string error, int attempt, TimeSpan retryDelay, CancellationToken ct = default);

    /// <summary>
    ///     Marks a non-terminal job as cancelled and clears its lease, so a worker holding a stale
    ///     snapshot cannot acquire and run it afterwards. A terminal job is left untouched.
    /// </summary>
    Task MarkCancelledAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Gets a job by ID.</summary>
    Task<JobInstance?> GetAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    ///     Deletes terminal jobs (Completed / Failed / Cancelled) that finished before
    ///     <paramref name="olderThan"/>, at most <paramref name="batchSize"/> per call. Returns the
    ///     number deleted.
    /// </summary>
    /// <remarks>
    ///     Without retention the job table grows by one row per execution forever — including every
    ///     recurring tick — and each row keeps its <c>ParametersJson</c> payload, which may carry
    ///     personal data with no expiry.
    /// </remarks>
    Task<int> PurgeTerminalAsync(DateTimeOffset olderThan, int batchSize, CancellationToken ct = default);
}
