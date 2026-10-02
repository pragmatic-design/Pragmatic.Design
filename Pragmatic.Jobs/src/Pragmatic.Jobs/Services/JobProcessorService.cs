using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.Diagnostics;
using Pragmatic.MultiTenancy;
using Pragmatic.Temporal.Clock;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Jobs.Services;

/// <summary>
///     BackgroundService that polls for pending jobs, acquires leases,
///     and executes them via the SG-generated <see cref="IJobTypeRegistry"/>.
/// </summary>
public sealed partial class JobProcessorService(
    IServiceScopeFactory scopeFactory,
    IJobTypeRegistry registry,
    IClock clock,
    JobsOptions options,
    ILogger<JobProcessorService> logger) : BackgroundService
{
    private readonly string _workerId = options.WorkerId ?? $"{Environment.MachineName}-{Guid.NewGuid().ToString("N")[..8]}";
    private readonly TimeSpan _leaseTime = TimeSpan.FromSeconds(options.LeaseTimeSeconds);

    // Per-host in-flight count per job type, gating [Job(MaxConcurrency = n)]. A job type over its
    // cap is left Pending for the next poll rather than blocking the worker.
    private readonly Dictionary<string, int> _typeInFlight = new();
    private readonly Lock _typeInFlightLock = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsurePersistenceMatchesConfiguration();

        LogProcessorStarted(_workerId, options.WorkerCount);

        // Wait for host startup to complete before polling. Configurable because a test that drives
        // this service pays the wait out of its own deadline (JobsOptions.StartupDelaySeconds).
        //
        // ⚠️ The zero branch yields rather than falling through. BackgroundService.StartAsync returns
        // at ExecuteAsync's first await, and Task.Delay(0) completes synchronously — so a zero delay
        // ran the first poll, the first claim and the first job body on the caller's thread before
        // StartAsync returned. Measured as a thirty-second timeout in JobTenantRestoreTests, once the
        // option existed and a test set it to zero.
        if (options.StartupDelaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(options.StartupDelaySeconds), stoppingToken)
                .ConfigureAwait(false);
        else
            await Task.Yield();

        // Semaphore is NOT in a using block: we dispose it only AFTER DrainInFlightAsync
        // so that lambdas already running can still call semaphore.Release() without
        // hitting ObjectDisposedException.
        var semaphore = new SemaphoreSlim(options.WorkerCount, options.WorkerCount);

        // Lock protects inFlight list: main loop calls RemoveAll while Task.Run
        // callbacks can complete concurrently and their continuations may be observed
        // by the same snapshot. Using a lock is the minimal, correct fix.
        var inFlight = new List<Task>();
        var inFlightLock = new Lock();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Resolve the scoped IJobStore (EF DbContext) under a dedicated per-poll scope so
                    // it is never captured by the singleton hosted service nor shared across workers.
                    var pending = await PollPendingAsync(stoppingToken).ConfigureAwait(false);

                    foreach (var job in pending)
                    {
                        await semaphore.WaitAsync(stoppingToken).ConfigureAwait(false);

                        var task = TryStart(job, () => semaphore.Release(), stoppingToken);
                        if (task is null)
                        {
                            semaphore.Release();
                            continue;
                        }

                        lock (inFlightLock)
                            inFlight.Add(task);
                    }

                    // Drop references to completed tasks so the list does not grow unbounded.
                    lock (inFlightLock)
                        inFlight.RemoveAll(static t => t.IsCompleted);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break; // Host shutting down
                }
                catch (Exception ex)
                {
                    LogPollError(ex);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(options.PollingIntervalSeconds), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            // Drain BEFORE disposing: in-flight lambdas still need to call semaphore.Release()
            Task[] snapshot;
            lock (inFlightLock)
                snapshot = inFlight.Where(static t => !t.IsCompleted).ToArray();

            await DrainInFlightAsync(snapshot).ConfigureAwait(false);

            semaphore.Dispose();
        }

        LogProcessorStopped(_workerId);
    }

    /// <summary>
    ///     Starts one job of a poll, unless its type is at its <c>[Job(MaxConcurrency = n)]</c> cap on
    ///     this host: then it is left Pending for the next poll, rather than hold a worker waiting.
    /// </summary>
    /// <param name="job">A job the poll returned.</param>
    /// <param name="onFinished">Called once the job has run, whatever the outcome.</param>
    /// <param name="ct">Cancelled when the host stops.</param>
    /// <returns>The running job, or null when the cap held it back.</returns>
    /// <remarks>
    ///     The slot is reserved here, synchronously, before anything is scheduled — so whether a job is
    ///     held back does not depend on when its task gets a thread. Internal because a test drives it
    ///     directly rather than through the polling loop, which would also assert the scheduler.
    /// </remarks>
    internal Task? TryStart(JobInstance job, Action onFinished, CancellationToken ct)
    {
        if (!TryReserveTypeSlot(job.JobType))
            return null;

        return Task.Run(async () =>
        {
            try
            {
                await ProcessJobAsync(job, ct).ConfigureAwait(false);
            }
            finally
            {
                ReleaseTypeSlot(job.JobType);
                onFinished();
            }
        }, ct);
    }

    // Reserves an execution slot for the job type, honouring [Job(MaxConcurrency = n)]. A type with
    // no cap (0) needs no tracking and always succeeds.
    private bool TryReserveTypeSlot(string jobType)
    {
        var max = registry.GetMaxConcurrency(jobType);
        if (max <= 0)
            return true;

        lock (_typeInFlightLock)
        {
            _typeInFlight.TryGetValue(jobType, out var current);
            if (current >= max)
                return false;
            _typeInFlight[jobType] = current + 1;
            return true;
        }
    }

    private void ReleaseTypeSlot(string jobType)
    {
        if (registry.GetMaxConcurrency(jobType) <= 0)
            return;

        lock (_typeInFlightLock)
        {
            if (_typeInFlight.TryGetValue(jobType, out var current) && current > 0)
                _typeInFlight[jobType] = current - 1;
        }
    }

    // An app that asked for durable jobs and silently got the in-memory store loses every queued
    // job on restart and cannot coordinate workers across hosts — a failure worth refusing to start
    // for, rather than discovering in production.
    private void EnsurePersistenceMatchesConfiguration()
    {
        if (!options.UseEfCore)
            return;

        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetService<IJobStore>();

        if (store is null or Stores.InMemoryJobStore)
        {
            throw new InvalidOperationException(
                "Jobs are configured with UseEfCore() but no durable IJobStore is registered. " +
                "Call UseEfCorePersistence() from the Pragmatic.Jobs.EFCore package (and apply " +
                "JobEntityTypeConfiguration / RecurringJobEntityTypeConfiguration to your DbContext), " +
                "or drop UseEfCore() to run on the in-memory stores.");
        }
    }

    private async Task DrainInFlightAsync(Task[] pending)
    {
        if (pending.Length == 0)
            return;

        LogDraining(_workerId, pending.Length);
        try
        {
            await Task.WhenAll(pending).WaitAsync(_leaseTime).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            LogDrainTimeout(_workerId, pending.Count(static t => !t.IsCompleted));
        }
        catch (Exception)
        {
            // Individual job failures are already logged by ProcessJobAsync; nothing to add here.
        }
    }

    /// <summary>
    ///     One poll: release what crashed workers left behind, purge if due, and take the jobs that are
    ///     ready to run.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Internal because a test drives it directly, together with <see cref="ProcessJobAsync" />,
    ///         instead of starting the hosted service and waiting for a poll to be scheduled.
    ///     </para>
    ///     <para>
    ///         ⚠️ That is not a convenience. A test that asserts what happens <b>inside</b> a job — the
    ///         tenant it runs under — through <c>StartAsync</c> is also asserting that the loop gets a
    ///         thread promptly, and on a loaded machine it may not: such a test times out while passing
    ///         alone. The two claims are separated here rather than measured together.
    ///     </para>
    /// </remarks>
    internal async Task<IReadOnlyList<JobInstance>> PollPendingAsync(CancellationToken ct)
    {
        // A dedicated scope so the scoped IJobStore (an EF DbContext) is never captured by the
        // singleton hosted service nor shared across workers.
        using var pollScope = scopeFactory.CreateScope();
        var pollStore = pollScope.ServiceProvider.GetRequiredService<IJobStore>();

        // Release expired leases from crashed workers
        await pollStore.ReleaseExpiredLeasesAsync(clock.UtcNow, ct).ConfigureAwait(false);

        await PurgeIfDueAsync(pollStore, ct).ConfigureAwait(false);

        return await pollStore.GetPendingAsync(options.BatchSize, clock.UtcNow, ct).ConfigureAwait(false);
    }

    internal async Task ProcessJobAsync(JobInstance job, CancellationToken ct)
    {
        // One DI scope per job: the scoped IJobStore (EF DbContext) is exclusive to this job, never
        // shared across the concurrent workers spawned by the poll loop.
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();

        // Acquire lease
        var acquired = await store.TryAcquireLeaseAsync(job.Id, _workerId, _leaseTime, ct).ConfigureAwait(false);
        if (!acquired)
        {
            JobsDiagnostics.LeaseConflicts.Add(1);
            return;
        }

        JobsDiagnostics.LeaseAcquisitions.Add(1);
        LogJobStarted(job.Id, job.JobType, job.Attempt);

        using var activity = JobsDiagnostics.ActivitySource.StartActivity($"Job.Execute.{job.JobType}");
        activity?.SetTag(JobTags.JobId, job.Id.ToString());
        activity?.SetTag(JobTags.JobType, job.JobType);
        activity?.SetTag(JobTags.RetryAttempt, job.Attempt);

        var stopwatch = Stopwatch.StartNew();

        // Keep the lease alive for as long as the job runs. Without this, any job outliving
        // LeaseTimeSeconds has its lease reaped by ReleaseExpiredLeasesAsync while still executing,
        // and a second worker starts the same job concurrently.
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = RenewLeaseWhileRunningAsync(job.Id, heartbeatCts.Token);

        try
        {
            var context = new JobContext(
                job.Id, job.JobType, job.ScheduledFor,
                job.Attempt, job.MaxAttempts,
                job.CorrelationId, job.TenantId);

            // Restore the job's originating tenant so any tenant-scoped EF query inside the job
            // resolves the correct filter. Without it the worker's null tenant leaks: cross-tenant
            // reads, and writes attributed to the wrong tenant.
            // ⚠️ TenantScope, not IMutableTenantContext.SetTenant: one mechanism for every worker,
            // rather than each resolving the mutable context from its own scope. TenantScope ships in
            // Abstractions, beside the contract it serves, so a module that references only
            // Abstractions can call it. The registered ITenantContext is AmbientTenantContext: it prefers the request tenant
            // and falls back to this ambient one, and outside a request there is no request tenant.
            using var tenantScope = job.TenantId is { Length: > 0 } jobTenant
                ? TenantScope.BeginScope(jobTenant)
                : null;

            await registry.ExecuteAsync(job.JobType, job.ParametersJson, context, scope.ServiceProvider, ct).ConfigureAwait(false);

            stopwatch.Stop();
            await store.MarkCompletedAsync(job.Id, _workerId, ct).ConfigureAwait(false);

            JobsDiagnostics.JobsCompleted.Add(1);
            JobsDiagnostics.JobDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Ok);
            LogJobCompleted(job.Id, job.JobType, stopwatch.Elapsed.TotalMilliseconds);

            // Enqueue continuation if declared. The type comes from the generator rather than the
            // job row, so [Continuation<T>] applies to jobs enqueued before it was added — the
            // column stays as the escape hatch for continuations built at enqueue time.
            var continuationJobType = job.ContinuationJobType ?? registry.GetContinuationJobType(job.JobType);
            if (!string.IsNullOrEmpty(continuationJobType))
                await EnqueueContinuationAsync(store, job, continuationJobType!, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host shutdown, not a job failure. Release the lease so another worker (or this one
            // after restart) picks the job straight back up instead of waiting out the lease.
            stopwatch.Stop();
            await store.ReleaseLeaseAsync(job.Id, _workerId, ct).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Unset);
            LogJobAbandonedOnShutdown(job.Id, job.JobType);
        }
        catch (OperationCanceledException ex)
        {
            // The job's own deadline fired ([Timeout]) while the host is still running. It counts as a
            // failed attempt: left uncaught, the row would stay Running until the lease expired and
            // then return to Pending with its attempt counter untouched — retrying forever without
            // ever reaching MaxAttempts.
            stopwatch.Stop();
            var timedOutAttempt = job.Attempt + 1;
            await store.MarkFailedAsync(job.Id, _workerId, $"Job timed out: {ex.Message}", timedOutAttempt,
                RetryDelayFor(job.JobType, timedOutAttempt), ct).ConfigureAwait(false);

            if (timedOutAttempt >= job.MaxAttempts)
            {
                JobsDiagnostics.JobsFailed.Add(1);
                LogJobTimedOut(job.Id, job.JobType, timedOutAttempt);
            }
            else
            {
                JobsDiagnostics.JobsRetried.Add(1);
                LogJobRetrying(job.Id, job.JobType, timedOutAttempt, job.MaxAttempts);
            }

            activity?.SetStatus(ActivityStatusCode.Error, "timeout");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var attempt = job.Attempt + 1;
            await store.MarkFailedAsync(job.Id, _workerId, ex.Message, attempt,
                RetryDelayFor(job.JobType, attempt), ct).ConfigureAwait(false);

            if (attempt >= job.MaxAttempts)
            {
                JobsDiagnostics.JobsFailed.Add(1);
                LogJobFailed(job.Id, job.JobType, attempt, ex);
            }
            else
            {
                JobsDiagnostics.JobsRetried.Add(1);
                LogJobRetrying(job.Id, job.JobType, attempt, job.MaxAttempts);
            }

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        }
        finally
        {
            // Stop renewing before the scope goes away, and observe the task so a renewal fault
            // surfaces as a log line rather than an unobserved exception.
            await heartbeatCts.CancelAsync().ConfigureAwait(false);
            await heartbeat.ConfigureAwait(false);
        }
    }

    // The job's declared [Retry] backoff, falling back to the store-wide default for jobs that
    // declare none. Policy lives here rather than in the store, which knows nothing about attributes.
    private TimeSpan RetryDelayFor(string jobType, int attempt)
        => registry.GetRetryPolicy(jobType)?.ComputeDelay(attempt) ?? JobRetryBackoff.Compute(attempt);

    // Purging is bookkeeping, not throughput: running it once an hour keeps it off the poll path
    // while still bounding table growth.
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);
    private DateTimeOffset _lastPurge = DateTimeOffset.MinValue;

    private async Task PurgeIfDueAsync(IJobStore store, CancellationToken ct)
    {
        if (options.RetentionDays <= 0)
            return;

        var now = clock.UtcNow;
        if (now - _lastPurge < PurgeInterval)
            return;

        _lastPurge = now;

        try
        {
            var cutoff = now.AddDays(-options.RetentionDays);
            await store.PurgeTerminalAsync(cutoff, options.PurgeBatchSize, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Retention is best-effort: a failed purge must never stop jobs from being processed.
            LogPurgeFailed(ex);
        }
    }

    // Renews at half the lease interval: one missed renewal still leaves a full half-lease of slack
    // before another worker could take the job over.
    private async Task RenewLeaseWhileRunningAsync(Guid jobId, CancellationToken ct)
    {
        var interval = TimeSpan.FromTicks(Math.Max(_leaseTime.Ticks / 2, TimeSpan.TicksPerSecond));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);

                // Dedicated scope: this runs concurrently with the job, which is using the
                // per-job scope's DbContext.
                using var scope = scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
                await store.RenewLeaseAsync(jobId, _workerId, _leaseTime, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // A failed renewal is not fatal: the job keeps running and the lease may expire,
                // but the fencing on MarkCompleted/MarkFailed still prevents a torn outcome.
                LogLeaseRenewalFailed(jobId, ex);
            }
        }
    }

    private async Task EnqueueContinuationAsync(IJobStore store, JobInstance completedJob, string continuationJobType, CancellationToken ct)
    {
        var continuation = new JobInstance
        {
            Id = Guid.NewGuid(),
            JobType = continuationJobType,
            ParametersJson = completedJob.ContinuationParametersJson,
            Status = JobStatus.Pending,
            ScheduledFor = clock.UtcNow,
            ParameterType = completedJob.ContinuationParametersJson is not null ? completedJob.ParameterType : null,
            // The continuation is its own job: it gets its own declared retry budget, not the
            // budget of the job that happened to trigger it.
            MaxAttempts = registry.GetRetryPolicy(continuationJobType)?.MaxAttempts ?? completedJob.MaxAttempts,
            CorrelationId = completedJob.CorrelationId,
            TenantId = completedJob.TenantId,
            CreatedAt = clock.UtcNow,
        };

        await store.EnqueueAsync(continuation, ct).ConfigureAwait(false);
        LogContinuationEnqueued(completedJob.Id, continuation.Id, continuation.JobType);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job retention purge failed; will retry next cycle")]
    partial void LogPurgeFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to renew lease for job {JobId}")]
    partial void LogLeaseRenewalFailed(Guid jobId, Exception ex);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Job {JobId} ({JobType}) abandoned on shutdown; lease released for immediate retry")]
    partial void LogJobAbandonedOnShutdown(Guid jobId, string jobType);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Job {JobId} ({JobType}) timed out on attempt {Attempt}")]
    partial void LogJobTimedOut(Guid jobId, string jobType, int attempt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job processor started: worker={WorkerId}, concurrency={WorkerCount}")]
    partial void LogProcessorStarted(string workerId, int workerCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job processor stopped: worker={WorkerId}")]
    partial void LogProcessorStopped(string workerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job processor draining: worker={WorkerId}, in-flight={InFlightCount}")]
    partial void LogDraining(string workerId, int inFlightCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job processor drain timed out: worker={WorkerId}, still running={StillRunning}")]
    partial void LogDrainTimeout(string workerId, int stillRunning);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Job {JobId} started: {JobType} (attempt {Attempt})")]
    partial void LogJobStarted(Guid jobId, string jobType, int attempt);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Job {JobId} completed: {JobType} in {DurationMs:F1}ms")]
    partial void LogJobCompleted(Guid jobId, string jobType, double durationMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {JobId} failed permanently: {JobType} after {Attempt} attempts")]
    partial void LogJobFailed(Guid jobId, string jobType, int attempt, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} retrying: {JobType} (attempt {Attempt}/{MaxAttempts})")]
    partial void LogJobRetrying(Guid jobId, string jobType, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Continuation enqueued: {CompletedJobId} → {ContinuationId} ({ContinuationType})")]
    partial void LogContinuationEnqueued(Guid completedJobId, Guid continuationId, string continuationType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job processor poll error")]
    partial void LogPollError(Exception ex);
}
