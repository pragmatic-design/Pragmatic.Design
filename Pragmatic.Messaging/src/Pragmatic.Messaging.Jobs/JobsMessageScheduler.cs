using Microsoft.Extensions.Logging;
using Pragmatic.Jobs;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Messaging.Jobs;

/// <summary>
///     <see cref="IMessageScheduler"/> implementation that delegates to <see cref="IJobScheduler"/>.
///     Serializes the message and schedules a <see cref="PublishMessageJob"/> for future execution.
/// </summary>
/// <remarks>
///     Scoped: captures the ambient <see cref="ITenantContext"/> so a message scheduled inside a tenant
///     request stays tenant-scoped when the background job publishes it later (the job runs with no
///     ambient tenant of its own — see <see cref="PublishMessageJob"/>).
/// </remarks>
public sealed partial class JobsMessageScheduler(
    IJobScheduler jobScheduler,
    IMessageSerializer serializer,
    ILogger<JobsMessageScheduler> logger,
    ITenantContext? tenantContext = null) : IMessageScheduler
{
    /// <inheritdoc />
    public Task<Guid> ScheduleAsync<T>(T message, TimeSpan delay, CancellationToken ct = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        var parameters = CreateParams(message);
        LogScheduling(parameters.MessageTypeName, delay);
        return jobScheduler.ScheduleAsync<PublishMessageJob, PublishMessageParams>(parameters, delay, ct: ct);
    }

    /// <inheritdoc />
    public Task<Guid> ScheduleAsync<T>(T message, DateTimeOffset scheduledAt, CancellationToken ct = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        var parameters = CreateParams(message);
        LogSchedulingAt(parameters.MessageTypeName, scheduledAt);
        return jobScheduler.ScheduleAtAsync<PublishMessageJob, PublishMessageParams>(parameters, scheduledAt, ct: ct);
    }

    /// <inheritdoc />
    public Task<Guid> ScheduleAsync<T>(T message, TimeSpan delay, MessageContext context, CancellationToken ct = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        var parameters = CreateParams(message) with
        {
            MessageId = context.MessageId,
            CorrelationId = context.CorrelationId,
            // Explicit context wins, but fall back to the ambient tenant when the caller left it unset
            // (mirrors the bus's publish-side capture) so the scheduled message is never unscoped.
            TenantId = context.TenantId ?? tenantContext?.TenantId,
            UserId = context.UserId,
            SourceBoundary = context.SourceBoundary,
            RetryCount = context.RetryCount,
        };
        LogScheduling(parameters.MessageTypeName, delay);
        return jobScheduler.ScheduleAsync<PublishMessageJob, PublishMessageParams>(parameters, delay, ct: ct);
    }

    /// <inheritdoc />
    public Task CancelAsync(Guid scheduleId, CancellationToken ct = default)
    {
        LogCancelling(scheduleId);
        return jobScheduler.CancelAsync(scheduleId, ct);
    }

    private PublishMessageParams CreateParams<T>(T message) where T : notnull
    {
        var messageType = typeof(T);
        var serialized = serializer.SerializeToString(message);
        return new PublishMessageParams(
            MessageTypeName: messageType.FullName ?? messageType.Name,
            SerializedMessage: serialized,
            // Stable dedupe id fixed at schedule time and persisted in the job params: every execution
            // AND retry of this scheduled message publishes with the SAME MessageId, so consumer
            // idempotency deduplicates a job-retry republish (independent of job-id retry semantics).
            MessageId: Guid.NewGuid().ToString("N"),
            // Capture the ambient tenant (see class remarks). Empty in single-tenant hosts.
            TenantId: tenantContext?.TenantId);
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Scheduling message {MessageType} with delay {Delay}")]
    private partial void LogScheduling(string messageType, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Scheduling message {MessageType} at {ScheduledAt}")]
    private partial void LogSchedulingAt(string messageType, DateTimeOffset scheduledAt);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Cancelling scheduled message {ScheduleId}")]
    private partial void LogCancelling(Guid scheduleId);
}
