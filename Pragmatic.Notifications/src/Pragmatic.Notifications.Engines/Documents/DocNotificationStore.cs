using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Notifications.Tracking;
using Pragmatic.Persistence.Documents;

namespace Pragmatic.Notifications.Engines.Documents;

/// <summary>
///     The <see cref="INotificationStore" /> of a <c>[StoresNotifications]</c> boundary the documents engine serves: what
///     <c>EfCoreNotificationStore</c> does, on the collection of the same name, through the boundary's session.
/// </summary>
/// <remarks>
///     A singleton, as the store is everywhere (the delivery worker is a hosted service), over a session that is scoped: each operation opens a
///     scope and reads the session the boundary is keyed by, as <c>EfCoreNotificationStore</c> creates a context per operation.
/// </remarks>
public sealed partial class DocNotificationStore(IServiceScopeFactory scopes, Type boundary, ILogger<DocNotificationStore> logger, TimeProvider? time = null)
    : INotificationStore
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<NotificationRecord> CreateAsync(NotificationRecord record, CancellationToken ct = default)
    {
        var scope = scopes.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        await Session(scope).InsertAsync(NotificationFields.From(new NotificationDocument(), record), ct).ConfigureAwait(false);
        return record;
    }

    /// <inheritdoc />
    public async Task UpdateStatusAsync(Guid notificationId, DeliveryStatus status, string? errorMessage, CancellationToken ct = default)
    {
        var scope = scopes.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        var session = Session(scope);
        var document = await session.Query<NotificationDocument>().FirstOrDefaultAsync((d, id) => d.Id == id, notificationId, ct).ConfigureAwait(false);
        if (document is null)
        {
            LogStatusUpdateMissed(notificationId, status);
            return;
        }

        NotificationFields.Transition(document, status, errorMessage, _time.GetUtcNow());
        await session.ReplaceAsync(document, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<NotificationRecord?> GetByIdAsync(Guid notificationId, CancellationToken ct = default)
    {
        var scope = scopes.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        var document = await Session(scope).Query<NotificationDocument>().FirstOrDefaultAsync((d, id) => d.Id == id, notificationId, ct)
            .ConfigureAwait(false);
        return document is null ? null : NotificationFields.ToRecord(document);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationRecord>> GetPendingAsync(int limit, CancellationToken ct = default)
    {
        var scope = scopes.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        var documents = await Session(scope).Query<NotificationDocument>().Where((d, s) => d.Status == s, nameof(DeliveryStatus.Pending))
            .OrderBy(d => d.CreatedAt).Take(limit).ToListAsync(ct).ConfigureAwait(false);
        return [.. documents.Select(NotificationFields.ToRecord)];
    }

    private DocSession Session(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredKeyedService<DocSession>(boundary);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Status update to {Status} affected no documents — notification record {NotificationId} not found")]
    private partial void LogStatusUpdateMissed(Guid notificationId, DeliveryStatus status);
}
