namespace Pragmatic.Notifications;

/// <summary>
///     Persistent record of a notification delivery attempt. Tracked in INotificationStore.
/// </summary>
public sealed record NotificationRecord
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required NotificationAudience Audience { get; init; }
    public required string RecipientAddress { get; init; }
    public required NotificationChannel Channel { get; init; }
    public required string Subject { get; init; }
    public required DeliveryStatus Status { get; init; }
    public string? ProviderId { get; init; }
    public string? ErrorMessage { get; init; }
    public string? TenantId { get; init; }
    public string? Category { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; init; }
    public DateTimeOffset? DeliveredAt { get; init; }
    public DateTimeOffset? ReadAt { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}
