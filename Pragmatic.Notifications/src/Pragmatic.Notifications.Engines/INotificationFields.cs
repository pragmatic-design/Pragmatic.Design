namespace Pragmatic.Notifications.Engines;

/// <summary>
///     The fields a notification record is stored with on each engine: <c>__Notifications</c>' columns, as EF Core's
///     <c>NotificationEntityTypeConfiguration</c> maps them (the three enums by name, the metadata as JSON), so the Sql row and the document
///     convert to and from a <see cref="NotificationRecord" /> through one mapping (<see cref="NotificationFields" />).
/// </summary>
public interface INotificationFields
{
    Guid Id { get; set; }

    string Audience { get; set; }

    string RecipientAddress { get; set; }

    string Channel { get; set; }

    string Subject { get; set; }

    string Status { get; set; }

    string? ProviderId { get; set; }

    string? ErrorMessage { get; set; }

    string? TenantId { get; set; }

    string? Category { get; set; }

    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset? SentAt { get; set; }

    DateTimeOffset? DeliveredAt { get; set; }

    DateTimeOffset? ReadAt { get; set; }

    string? MetadataJson { get; set; }
}
