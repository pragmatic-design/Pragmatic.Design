using System.Collections.Concurrent;

namespace Pragmatic.Notifications.Tracking;

/// <summary>
///     In-memory notification store for development and testing. Not suitable for production.
/// </summary>
internal sealed class InMemoryNotificationStore : INotificationStore
{
    private readonly ConcurrentDictionary<Guid, NotificationRecord> _records = new();

    public Task<NotificationRecord> CreateAsync(NotificationRecord record, CancellationToken ct = default)
    {
        _records[record.Id] = record;
        return Task.FromResult(record);
    }

    public Task UpdateStatusAsync(Guid notificationId, DeliveryStatus status, string? errorMessage, CancellationToken ct = default)
    {
        // Atomic compare-and-swap loop: read the current value, compute the new value, swap atomically.
        NotificationRecord? existing;
        while (_records.TryGetValue(notificationId, out existing))
        {
            var updated = existing with
            {
                Status = status,
                ErrorMessage = errorMessage,
                SentAt = status == DeliveryStatus.Sent ? DateTimeOffset.UtcNow : existing.SentAt,
                DeliveredAt = status == DeliveryStatus.Delivered ? DateTimeOffset.UtcNow : existing.DeliveredAt,
                ReadAt = status == DeliveryStatus.Read ? DateTimeOffset.UtcNow : existing.ReadAt,
            };
            if (_records.TryUpdate(notificationId, updated, existing))
                break;
        }

        return Task.CompletedTask;
    }

    public Task<NotificationRecord?> GetByIdAsync(Guid notificationId, CancellationToken ct = default)
        => Task.FromResult(_records.GetValueOrDefault(notificationId));

    public Task<IReadOnlyList<NotificationRecord>> GetPendingAsync(int limit, CancellationToken ct = default)
    {
        var pending = _records.Values
            .Where(r => r.Status == DeliveryStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<NotificationRecord>>(pending);
    }
}
