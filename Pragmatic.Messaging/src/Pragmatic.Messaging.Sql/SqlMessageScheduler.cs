using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging.Sql;

/// <summary>
///     <see cref="IMessageScheduler"/> on the SQL transport's own queue table: scheduling
///     fan-outs the rows immediately with <c>VisibleAt = scheduledAt</c> (invisible until due)
///     and stamps a <c>SchedulingTokenId</c>. Cancellation deletes the unclaimed rows by token —
///     DURABLE and restart-safe, unlike the ASB scheduler's in-process handle map.
/// </summary>
/// <remarks>
///     Documented races: an already-claimed row can no longer be cancelled; subscriptions
///     registered AFTER scheduling do not receive the message (fan-out happens at schedule time).
/// </remarks>
public sealed partial class SqlMessageScheduler(
    SqlTransportStorage storage,
    IMessageRouter router,
    IMessageSerializer serializer,
    ILogger<SqlMessageScheduler> logger) : IMessageScheduler
{
    /// <inheritdoc />
    public Task<Guid> ScheduleAsync<T>(T message, TimeSpan delay, CancellationToken ct = default)
        where T : notnull
        => ScheduleAsync(message, DateTimeOffset.UtcNow + delay, ct);

    /// <inheritdoc />
    public async Task<Guid> ScheduleAsync<T>(T message, DateTimeOffset scheduledAt, CancellationToken ct = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        return await ScheduleCoreAsync(message, scheduledAt, MessageContext.New(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Guid> ScheduleAsync<T>(T message, TimeSpan delay, MessageContext context, CancellationToken ct = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        return await ScheduleCoreAsync(message, DateTimeOffset.UtcNow + delay, context, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CancelAsync(Guid scheduleId, CancellationToken ct = default)
    {
        var cancelled = await storage.CancelScheduledAsync(scheduleId, ct).ConfigureAwait(false);
        LogCancelled(scheduleId, cancelled);
    }

    private async Task<Guid> ScheduleCoreAsync<T>(T message, DateTimeOffset scheduledAt, MessageContext context, CancellationToken ct)
        where T : notnull
    {
        var topic = router.GetTopic<T>();
        var payload = serializer.Serialize(message, typeof(T));
        var token = Guid.NewGuid();

        await storage.PublishAsync(payload, topic, context, visibleAt: scheduledAt, schedulingToken: token, ct)
            .ConfigureAwait(false);

        LogScheduled(typeof(T).Name, topic, scheduledAt, token);
        return token;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Scheduled {MessageType} on {Topic} for {ScheduledAt} (token {Token}, durable cancel)")]
    private partial void LogScheduled(string messageType, string topic, DateTimeOffset scheduledAt, Guid token);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cancelled schedule {Token}: {Rows} unclaimed row(s) removed")]
    private partial void LogCancelled(Guid token, int rows);
}
