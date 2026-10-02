namespace Pragmatic.Notifications.Pipeline;

/// <summary>
///     Determines which channels to use for a resolved recipient based on priority, preferences, and category.
/// </summary>
public interface INotificationRouter
{
    /// <summary>
    ///     Routes a notification to the appropriate channels for the given recipient.
    /// </summary>
    NotificationChannel Route(
        ResolvedRecipient recipient,
        NotificationPriority priority,
        string? category);
}
