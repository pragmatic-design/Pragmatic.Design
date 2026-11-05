using System.Text.Json;
using Pragmatic.Serialization;

namespace Pragmatic.Notifications.Engines;

/// <summary>
///     A <see cref="NotificationRecord" /> to and from the stored fields of either engine, as <c>EfCoreNotificationStore</c> converts it: the
///     enums by name, the metadata through the common JSON context.
/// </summary>
internal static class NotificationFields
{
    /// <summary>Fills <paramref name="fields" /> from <paramref name="record" />.</summary>
    public static T From<T>(T fields, NotificationRecord record) where T : INotificationFields
    {
        fields.Id = record.Id;
        fields.Audience = record.Audience.ToString();
        fields.RecipientAddress = record.RecipientAddress;
        fields.Channel = record.Channel.ToString();
        fields.Subject = record.Subject;
        fields.Status = record.Status.ToString();
        fields.ProviderId = record.ProviderId;
        fields.ErrorMessage = record.ErrorMessage;
        fields.TenantId = record.TenantId;
        fields.Category = record.Category;
        fields.CreatedAt = record.CreatedAt;
        fields.SentAt = record.SentAt;
        fields.DeliveredAt = record.DeliveredAt;
        fields.ReadAt = record.ReadAt;
        fields.MetadataJson = record.Metadata is null ? null : JsonSerializer.Serialize(record.Metadata, PragmaticCommonJsonContext.Default.DictionaryStringString);
        return fields;
    }

    /// <summary>The record <paramref name="fields" /> hold.</summary>
    public static NotificationRecord ToRecord(INotificationFields fields) => new()
    {
        Id = fields.Id,
        Audience = Enum.Parse<NotificationAudience>(fields.Audience),
        RecipientAddress = fields.RecipientAddress,
        Channel = Enum.Parse<NotificationChannel>(fields.Channel),
        Subject = fields.Subject,
        Status = Enum.Parse<DeliveryStatus>(fields.Status),
        ProviderId = fields.ProviderId,
        ErrorMessage = fields.ErrorMessage,
        TenantId = fields.TenantId,
        Category = fields.Category,
        CreatedAt = fields.CreatedAt,
        SentAt = fields.SentAt,
        DeliveredAt = fields.DeliveredAt,
        ReadAt = fields.ReadAt,
        Metadata = fields.MetadataJson is null ? null : JsonSerializer.Deserialize(fields.MetadataJson, PragmaticCommonJsonContext.Default.DictionaryStringString),
    };

    /// <summary>
    ///     A status transition, as <c>EfCoreNotificationStore.UpdateStatusAsync</c> applies it: the status and the error written, and only the
    ///     timestamp of the status reached stamped; the others are left as they were.
    /// </summary>
    public static void Transition(INotificationFields fields, DeliveryStatus status, string? errorMessage, DateTimeOffset now)
    {
        fields.Status = status.ToString();
        fields.ErrorMessage = errorMessage;
        if (status == DeliveryStatus.Sent)
            fields.SentAt = now;
        else if (status == DeliveryStatus.Delivered)
            fields.DeliveredAt = now;
        else if (status == DeliveryStatus.Read)
            fields.ReadAt = now;
    }
}
