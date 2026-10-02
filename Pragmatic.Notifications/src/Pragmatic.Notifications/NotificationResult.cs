namespace Pragmatic.Notifications;

/// <summary>
///     Result of a notification send/enqueue operation.
/// </summary>
public sealed record NotificationResult
{
    /// <summary>Whether the notification was accepted for delivery.</summary>
    public required bool Success { get; init; }

    /// <summary>Unique ID for tracking this notification.</summary>
    public Guid? NotificationId { get; init; }

    /// <summary>IDs of individual delivery attempts (one per recipient × channel).</summary>
    public IReadOnlyList<Guid>? DeliveryIds { get; init; }

    /// <summary>Errors encountered during delivery.</summary>
    public IReadOnlyList<string>? Errors { get; init; }

    /// <summary>Creates a successful result.</summary>
    public static NotificationResult Succeeded(Guid notificationId, IReadOnlyList<Guid>? deliveryIds = null) => new()
    {
        Success = true,
        NotificationId = notificationId,
        DeliveryIds = deliveryIds,
    };

    /// <summary>
    ///     Creates a partially-successful result: at least one delivery succeeded, the
    ///     failures are listed in <see cref="Errors"/>. Never masked as a clean success.
    /// </summary>
    public static NotificationResult Partial(Guid notificationId, IReadOnlyList<Guid> deliveryIds, params string[] errors) => new()
    {
        Success = true,
        NotificationId = notificationId,
        DeliveryIds = deliveryIds,
        Errors = errors,
    };

    /// <summary>Creates a failed result.</summary>
    public static NotificationResult Failed(params string[] errors) => new()
    {
        Success = false,
        Errors = errors,
    };
}
