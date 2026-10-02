using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pragmatic.Notifications.Tracking;

namespace Pragmatic.Notifications.EFCore;

/// <summary>
///     EF Core implementation of <see cref="INotificationStore"/>. Persists notification records to a database.
/// </summary>
internal sealed partial class EfCoreNotificationStore(
    IDbContextFactory<NotificationDbContext> factory,
    ILogger<EfCoreNotificationStore> logger)
    : INotificationStore
{
    private readonly ILogger<EfCoreNotificationStore> _logger = logger;

    public async Task<NotificationRecord> CreateAsync(NotificationRecord record, CancellationToken ct = default)
    {
        var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var entity = ToEntity(record);
            db.Notifications.Add(entity);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return record;
    }

    public async Task UpdateStatusAsync(Guid notificationId, DeliveryStatus status, string? errorMessage, CancellationToken ct = default)
    {
        var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // Each timestamp is stamped when its own status is reached and otherwise left as it is.
            // Writing null on the other branches (as this did) meant every transition wiped the
            // earlier stamps: moving a record from Sent to Delivered erased SentAt, and a later
            // Failed erased all of them. The in-memory store preserves them, so unit tests against
            // it could never surface the divergence.
            var now = DateTimeOffset.UtcNow;

            var affected = await db.Notifications
                .Where(n => n.Id == notificationId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Status, status)
                    .SetProperty(n => n.ErrorMessage, errorMessage)
                    .SetProperty(n => n.SentAt, n => status == DeliveryStatus.Sent ? now : n.SentAt)
                    .SetProperty(n => n.DeliveredAt, n => status == DeliveryStatus.Delivered ? now : n.DeliveredAt)
                    .SetProperty(n => n.ReadAt, n => status == DeliveryStatus.Read ? now : n.ReadAt),
                    ct)
                .ConfigureAwait(false);

            // A missed update (record not yet visible, or deleted) would otherwise leave the
            // status transition silently lost — surface it so the Pending record is explainable.
            if (affected == 0)
                LogStatusUpdateMissed(notificationId, status);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Status update to {Status} affected no rows — notification record {NotificationId} not found")]
    private partial void LogStatusUpdateMissed(Guid notificationId, DeliveryStatus status);

    public async Task<NotificationRecord?> GetByIdAsync(Guid notificationId, CancellationToken ct = default)
    {
        var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var entity = await db.Notifications
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == notificationId, ct)
                .ConfigureAwait(false);

            return entity is not null ? ToRecord(entity) : null;
        }
    }

    public async Task<IReadOnlyList<NotificationRecord>> GetPendingAsync(int limit, CancellationToken ct = default)
    {
        var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var entities = await db.Notifications
                .AsNoTracking()
                .Where(n => n.Status == DeliveryStatus.Pending)
                .OrderBy(n => n.CreatedAt)
                .Take(limit)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return entities.Select(ToRecord).ToList();
        }
    }

    private static NotificationEntity ToEntity(NotificationRecord record) => new()
    {
        Id = record.Id,
        Audience = record.Audience,
        RecipientAddress = record.RecipientAddress,
        Channel = record.Channel,
        Subject = record.Subject,
        Status = record.Status,
        ProviderId = record.ProviderId,
        ErrorMessage = record.ErrorMessage,
        TenantId = record.TenantId,
        Category = record.Category,
        CreatedAt = record.CreatedAt,
        SentAt = record.SentAt,
        DeliveredAt = record.DeliveredAt,
        ReadAt = record.ReadAt,
        MetadataJson = record.Metadata is not null
            ? JsonSerializer.Serialize(record.Metadata, global::Pragmatic.Serialization.PragmaticCommonJsonContext.Default.DictionaryStringString)
            : null,
    };

    private static NotificationRecord ToRecord(NotificationEntity entity) => new()
    {
        Id = entity.Id,
        Audience = entity.Audience,
        RecipientAddress = entity.RecipientAddress,
        Channel = entity.Channel,
        Subject = entity.Subject,
        Status = entity.Status,
        ProviderId = entity.ProviderId,
        ErrorMessage = entity.ErrorMessage,
        TenantId = entity.TenantId,
        Category = entity.Category,
        CreatedAt = entity.CreatedAt,
        SentAt = entity.SentAt,
        DeliveredAt = entity.DeliveredAt,
        ReadAt = entity.ReadAt,
        Metadata = entity.MetadataJson is not null
            ? JsonSerializer.Deserialize(entity.MetadataJson, global::Pragmatic.Serialization.PragmaticCommonJsonContext.Default.DictionaryStringString)
            : null,
    };
}
