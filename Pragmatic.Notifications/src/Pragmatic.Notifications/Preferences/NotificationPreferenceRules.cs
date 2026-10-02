namespace Pragmatic.Notifications.Preferences;

/// <summary>
///     Decides whether a recipient's preferences suppress a notification.
/// </summary>
/// <remarks>
///     Lives outside the router because suppression is a policy of the pipeline, not of channel
///     selection: it must hold even when the caller pins the channel with
///     <see cref="NotificationRequest.ChannelOverride"/>, which bypasses routing entirely. A muted
///     category or an opt-out is a promise to the recipient, not a routing hint.
/// </remarks>
internal static class NotificationPreferenceRules
{
    public static bool IsSuppressed(
        NotificationPreferences? preferences,
        NotificationPriority priority,
        string? category)
    {
        if (preferences is not { } prefs)
            return false;

        if (!prefs.Enabled)
            return true;

        // Do-not-disturb holds everything below Critical — Critical bypasses by contract (enum docs).
        if (prefs.DoNotDisturb && priority < NotificationPriority.Critical)
            return true;

        if (category is not null && prefs.MutedCategories?.Contains(category) == true)
            return true;

        return false;
    }
}
