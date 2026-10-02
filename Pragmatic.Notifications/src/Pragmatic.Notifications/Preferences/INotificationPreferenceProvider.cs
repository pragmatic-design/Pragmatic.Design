namespace Pragmatic.Notifications.Preferences;

/// <summary>
///     Provides user notification preferences. Override to integrate with Identity or custom user profiles.
/// </summary>
public interface INotificationPreferenceProvider
{
    /// <summary>Gets preferences for a user (by user ID or address).</summary>
    Task<NotificationPreferences?> GetPreferencesAsync(string userIdOrAddress, CancellationToken ct = default);
}
