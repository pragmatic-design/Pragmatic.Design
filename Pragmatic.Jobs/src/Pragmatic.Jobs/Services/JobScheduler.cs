using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.Diagnostics;
using Pragmatic.MultiTenancy;
using Pragmatic.Serialization;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Jobs.Services;

/// <summary>
///     Default implementation of <see cref="IJobScheduler"/>.
///     Creates <see cref="JobInstance"/> and enqueues via <see cref="IJobStore"/>.
/// </summary>
public sealed partial class JobScheduler(
    IJobStore store,
    IClock clock,
    JobsOptions options,
    ILogger<JobScheduler> logger,
    PragmaticJsonOptions jsonOptions,
    IJobTypeRegistry registry,
    // Optional, because Pragmatic.MultiTenancy is. An application that registered no tenant context
    // schedules exactly as it did before; Microsoft's container fills a constructor parameter it
    // cannot resolve from its default value, which is what makes null reachable here.
    //
    // ⚠️ The contract is in Pragmatic.Abstractions, which this module already references — the
    // implementation is what lives in the runtime package. No new dependency was needed for this.
    ITenantContext? tenantContext = null) : IJobScheduler
{
    private readonly JsonSerializerOptions _jsonOptions = (jsonOptions ?? PragmaticJsonOptions.Default).Build();

    // AOT-clean: resolve the parameter type's JsonTypeInfo from the seam (the generated context covers it)
    // and use the JsonTypeInfo-based overload, which carries no IL2026/IL3050.
    private string SerializeParams<TParams>(TParams parameters) where TParams : notnull
        => JsonSerializer.Serialize(parameters, (JsonTypeInfo<TParams>)_jsonOptions.GetTypeInfo(typeof(TParams)));

    /// <inheritdoc />
    public Task<Guid> ScheduleAsync<TJob>(
        TimeSpan? delay = null, string? correlationId = null,
        JobContinuation? continuation = null, CancellationToken ct = default)
        where TJob : IJob
    {
        var job = CreateInstance(GetTypeName(typeof(TJob)), null, null, delay, correlationId, continuation);
        return EnqueueAndReturn(job, ct);
    }

    /// <inheritdoc />
    public Task<Guid> ScheduleAsync<TJob, TParams>(
        TParams parameters, TimeSpan? delay = null, string? correlationId = null,
        JobContinuation? continuation = null, CancellationToken ct = default)
        where TJob : IJob<TParams>
        where TParams : notnull
    {
        var json = SerializeParams(parameters);
        var job = CreateInstance(
            GetTypeName(typeof(TJob)), json, typeof(TParams).FullName, delay, correlationId, continuation);
        return EnqueueAndReturn(job, ct);
    }

    /// <inheritdoc />
    public Task<Guid> ScheduleAtAsync<TJob>(
        DateTimeOffset scheduledFor, string? correlationId = null,
        JobContinuation? continuation = null, CancellationToken ct = default)
        where TJob : IJob
    {
        var job = CreateInstance(GetTypeName(typeof(TJob)), null, null, null, correlationId, continuation);
        job.ScheduledFor = scheduledFor.ToUniversalTime();
        return EnqueueAndReturn(job, ct);
    }

    /// <inheritdoc />
    public Task<Guid> ScheduleAtAsync<TJob, TParams>(
        TParams parameters, DateTimeOffset scheduledFor, string? correlationId = null,
        JobContinuation? continuation = null, CancellationToken ct = default)
        where TJob : IJob<TParams>
        where TParams : notnull
    {
        var json = SerializeParams(parameters);
        var job = CreateInstance(
            GetTypeName(typeof(TJob)), json, typeof(TParams).FullName, null, correlationId, continuation);
        job.ScheduledFor = scheduledFor.ToUniversalTime();
        return EnqueueAndReturn(job, ct);
    }

    private static string GetTypeName(Type type)
        => type.FullName
            ?? throw new InvalidOperationException($"Cannot determine the fully-qualified name of {type.Name}. Generic or anonymous types are not supported as job types.");

    /// <inheritdoc />
    public async Task CancelAsync(Guid jobId, CancellationToken ct = default)
    {
        await store.MarkCancelledAsync(jobId, ct).ConfigureAwait(false);
        LogJobCancelled(jobId);
    }

    private JobInstance CreateInstance(
        string jobType, string? paramsJson, string? paramType, TimeSpan? delay, string? correlationId,
        JobContinuation? continuation)
    {
        // ⚠️ Refused here, where the caller is, and not in the background where the failure becomes a
        // column nobody reads. A row scheduled for a type no registry knows cannot ever run, and the
        // message the messaging bridge was carrying has already been acknowledged by then — so the
        // only trace of it is an error on a job.
        if (!registry.Knows(jobType))
            throw new InvalidOperationException(
                $"No job type registry knows '{jobType}', so scheduling it would create work that can "
                + "never run. Declare it with [Job], or reference and register the package that ships it.");

        // Capture once so ScheduledFor and CreatedAt always share the same timestamp.
        var now = clock.UtcNow;
        return new JobInstance
        {
            Id = Guid.NewGuid(),
            JobType = jobType,
            ParametersJson = paramsJson,
            ParameterType = paramType,
            Status = JobStatus.Pending,
            ScheduledFor = delay.HasValue ? now.Add(delay.Value) : now,
            Priority = registry.GetPriority(jobType),
            // A job's own [Retry(MaxAttempts = n)] governs its durable attempt budget; the option
            // is only the fallback for jobs that declare none. Written to the persisted row, so
            // [Retry] and the visible Attempt count agree.
            MaxAttempts = registry.GetRetryPolicy(jobType)?.MaxAttempts ?? options.DefaultMaxRetries,
            CorrelationId = correlationId,
            CreatedAt = now,
            // The tenant the caller is in, captured here the way CreatedAt captures the clock. The
            // run happens outside the request, so this is the only moment it can be known.
            //
            // ⚠️ Without it every query the job makes reads ZERO ROWS in a multi-tenant application —
            // not an error, not an empty database — and the job reports success on having done
            // nothing. JobInstance.TenantId existed and only the recurring scheduler and a
            // continuation ever wrote it; the ad-hoc path, which is the one an application uses,
            // left it null.
            //
            // No explicit parameter on IJobScheduler on purpose: TenantScope.BeginScope already says
            // "do this as that tenant", the registered context falls back to it outside a request,
            // and a second way to say the same thing is a second thing to keep true.
            TenantId = tenantContext?.TenantId,
            // Persisted with the job, so JobProcessorService finds the chain on the row rather than in
            // the memory of the process that scheduled it. Its own lookup —
            // registry.GetContinuationJobType — stays the fallback for a job whose continuation is
            // declared by attribute instead of passed here.
            ContinuationJobType = continuation?.JobType,
            ContinuationParametersJson = continuation?.ParametersJson,
        };
    }

    private async Task<Guid> EnqueueAndReturn(JobInstance job, CancellationToken ct)
    {
        await store.EnqueueAsync(job, ct).ConfigureAwait(false);
        JobsDiagnostics.JobsEnqueued.Add(1);
        LogJobScheduled(job.Id, job.JobType, job.ScheduledFor);
        return job.Id;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Job {JobId} scheduled: {JobType} at {ScheduledFor}")]
    partial void LogJobScheduled(Guid jobId, string jobType, DateTimeOffset scheduledFor);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Job {JobId} cancelled")]
    partial void LogJobCancelled(Guid jobId);
}
