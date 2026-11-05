using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Notifications.Engines.Documents;

/// <summary>
///     A notification record of a <c>[StoresNotifications]</c> boundary the documents engine serves (collection <c>__Notifications</c>), read and
///     written by <see cref="DocNotificationStore" />. The fields are <see cref="Sql.NotificationRow" />'s columns, except the instants: UTC
///     ticks, as <c>OutboxMessageDocument</c>'s, because a BSON datetime keeps milliseconds only and a record would not read back the instant it
///     was written with. <see cref="INotificationFields" /> sees them as instants, through its explicit members.
/// </summary>
[Document("__Notifications")]
public sealed partial class NotificationDocument : INotificationFields
{
    /// <summary>The collection in a boundary's <c>{Boundary}DocSchema</c>: the indexes EF Core's configuration declares, with its names.</summary>
    public static readonly DocCollectionSchema Schema = new("__Notifications", "/id",
    [
        new DocIndex("IX___Notifications_Status", ["Status"]),
        new DocIndex("IX___Notifications_CreatedAt", ["CreatedAt"]),
        new DocIndex("IX___Notifications_TenantId", ["TenantId"]),
        new DocIndex("IX___Notifications_Status_CreatedAt", ["Status", "CreatedAt"]),
    ], []);

    [Key]
    public Guid Id { get; set; }

    public string Audience { get; set; } = "";

    public string RecipientAddress { get; set; } = "";

    public string Channel { get; set; } = "";

    public string Subject { get; set; } = "";

    public string Status { get; set; } = "";

    public string? ProviderId { get; set; }

    public string? ErrorMessage { get; set; }

    public string? TenantId { get; set; }

    public string? Category { get; set; }

    /// <summary>When the record was created, in UTC ticks: the order pending records are delivered in.</summary>
    public long CreatedAt { get; set; }

    public long? SentAt { get; set; }

    public long? DeliveredAt { get; set; }

    public long? ReadAt { get; set; }

    public string? MetadataJson { get; set; }

    DateTimeOffset INotificationFields.CreatedAt
    {
        get => new(CreatedAt, TimeSpan.Zero);
        set => CreatedAt = value.UtcTicks;
    }

    DateTimeOffset? INotificationFields.SentAt
    {
        get => Instant(SentAt);
        set => SentAt = value?.UtcTicks;
    }

    DateTimeOffset? INotificationFields.DeliveredAt
    {
        get => Instant(DeliveredAt);
        set => DeliveredAt = value?.UtcTicks;
    }

    DateTimeOffset? INotificationFields.ReadAt
    {
        get => Instant(ReadAt);
        set => ReadAt = value?.UtcTicks;
    }

    private static DateTimeOffset? Instant(long? ticks) => ticks is { } t ? new DateTimeOffset(t, TimeSpan.Zero) : null;
}
