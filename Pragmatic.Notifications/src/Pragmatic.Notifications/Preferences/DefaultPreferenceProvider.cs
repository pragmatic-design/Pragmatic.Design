namespace Pragmatic.Notifications.Preferences;

/// <summary>
///     Default preference provider — all notifications enabled, no muting, email preferred.
/// </summary>
internal sealed class DefaultPreferenceProvider : INotificationPreferenceProvider
{
    private static readonly NotificationPreferences DefaultPrefs = new();

    public Task<NotificationPreferences?> GetPreferencesAsync(string userIdOrAddress, CancellationToken ct = default)
        => Task.FromResult<NotificationPreferences?>(DefaultPrefs);
}
