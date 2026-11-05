using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Notifications.Tracking;
using Pragmatic.Persistence.Sql;

namespace Pragmatic.Notifications.Engines.Sql;

/// <summary>
///     The <see cref="INotificationStore" /> of a <c>[StoresNotifications]</c> boundary the Sql engine serves: what <c>EfCoreNotificationStore</c>
///     does, on the same table, through the boundary's session.
/// </summary>
/// <remarks>
///     A singleton, as the store is everywhere (the delivery worker is a hosted service), over a session that is scoped: each operation opens a
///     scope and reads the session the boundary is keyed by, as <c>EfCoreNotificationStore</c> creates a context per operation.
/// </remarks>
public sealed partial class SqlNotificationStore(IServiceScopeFactory scopes, Type boundary, ILogger<SqlNotificationStore> logger, TimeProvider? time = null)
    : INotificationStore
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<NotificationRecord> CreateAsync(NotificationRecord record, CancellationToken ct = default)
    {
        var scope = scopes.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        await Session(scope).InsertAsync(NotificationFields.From(new NotificationRow(), record), ct).ConfigureAwait(false);
        return record;
    }

    /// <inheritdoc />
    public async Task UpdateStatusAsync(Guid notificationId, DeliveryStatus status, string? errorMessage, CancellationToken ct = default)
    {
        var scope = scopes.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        var session = Session(scope);
        var row = await session.Query<NotificationRow>().FirstOrDefaultAsync((r, id) => r.Id == id, notificationId, ct).ConfigureAwait(false);
        if (row is null)
        {
            LogStatusUpdateMissed(notificationId, status);
            return;
        }

        NotificationFields.Transition(row, status, errorMessage, _time.GetUtcNow());
        await session.UpdateAsync(row, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<NotificationRecord?> GetByIdAsync(Guid notificationId, CancellationToken ct = default)
    {
        var scope = scopes.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        var row = await Session(scope).Query<NotificationRow>().FirstOrDefaultAsync((r, id) => r.Id == id, notificationId, ct).ConfigureAwait(false);
        return row is null ? null : NotificationFields.ToRecord(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationRecord>> GetPendingAsync(int limit, CancellationToken ct = default)
    {
        var scope = scopes.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        var rows = await Session(scope).Query<NotificationRow>().Where((r, s) => r.Status == s, nameof(DeliveryStatus.Pending))
            .OrderBy(r => r.CreatedAt).Take(limit).ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(NotificationFields.ToRecord)];
    }

    private SqlSession Session(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredKeyedService<SqlSession>(boundary);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Status update to {Status} affected no rows — notification record {NotificationId} not found")]
    private partial void LogStatusUpdateMissed(Guid notificationId, DeliveryStatus status);
}
