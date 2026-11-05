using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Notifications.Engines.Sql;

/// <summary>
///     A notification record of a <c>[StoresNotifications]</c> boundary the Sql engine serves (<c>__Notifications</c>), read and written by
///     <see cref="SqlNotificationStore" />. The table is EF Core's (<c>NotificationEntityTypeConfiguration</c>), column for column: the three enums
///     by name, the lengths its configuration gives, so a database holds one <c>__Notifications</c> whichever engine writes it.
/// </summary>
[Entity]
[Table("__Notifications")]
public sealed class NotificationRow : INotificationFields
{
    public Guid Id { get; set; }

    [MaxLength(32)]
    public string Audience { get; set; } = "";

    [MaxLength(512)]
    public string RecipientAddress { get; set; } = "";

    [MaxLength(32)]
    public string Channel { get; set; } = "";

    [MaxLength(1024)]
    public string Subject { get; set; } = "";

    [MaxLength(32)]
    public string Status { get; set; } = "";

    [MaxLength(256)]
    public string? ProviderId { get; set; }

    [MaxLength(2048)]
    public string? ErrorMessage { get; set; }

    [MaxLength(128)]
    public string? TenantId { get; set; }

    [MaxLength(256)]
    public string? Category { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>The record's metadata, as JSON; unbounded, as the configuration's <c>text</c> column.</summary>
    public string? MetadataJson { get; set; }
}
