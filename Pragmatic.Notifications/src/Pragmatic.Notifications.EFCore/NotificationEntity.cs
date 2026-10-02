namespace Pragmatic.Notifications.EFCore;

/// <summary>
///     EF Core entity for the __Notifications table.
/// </summary>
internal sealed class NotificationEntity
{
    public Guid Id { get; set; }
    public NotificationAudience Audience { get; set; }
    public string RecipientAddress { get; set; } = null!;
    public NotificationChannel Channel { get; set; }
    public string Subject { get; set; } = null!;
    public DeliveryStatus Status { get; set; }
    public string? ProviderId { get; set; }
    public string? ErrorMessage { get; set; }
    public string? TenantId { get; set; }
    public string? Category { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    /// <summary>Serialized JSON column for arbitrary notification metadata.</summary>
    public string? MetadataJson { get; set; }
}
