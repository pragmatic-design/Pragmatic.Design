namespace Pragmatic.Notifications.Channels;

/// <summary>
///     Resolves <see cref="INotificationChannel"/> instances, potentially per-tenant.
/// </summary>
public interface INotificationChannelFactory
{
    /// <summary>
    ///     Gets the channel provider for the given channel type and optional tenant.
    /// </summary>
    INotificationChannel? GetChannel(NotificationChannel channel, string? tenantId = null);

    /// <summary>
    ///     Returns all registered channel types.
    /// </summary>
    IReadOnlyList<NotificationChannel> GetRegisteredChannels();
}
