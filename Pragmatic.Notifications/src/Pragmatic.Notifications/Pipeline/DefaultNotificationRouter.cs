using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Preferences;

namespace Pragmatic.Notifications.Pipeline;

/// <summary>
///     Default routing strategy: maps priority to channels based on preferences and registered channels.
/// </summary>
internal sealed class DefaultNotificationRouter(INotificationChannelFactory channelFactory) : INotificationRouter
{
    public NotificationChannel Route(
        ResolvedRecipient recipient,
        NotificationPriority priority,
        string? category)
    {
        var registered = channelFactory.GetRegisteredChannels();
        if (registered.Count == 0)
            return NotificationChannel.None;

        // User preferences: disabled, do-not-disturb, muted category. The pipeline applies the same
        // rule independently (it must also hold when ChannelOverride skips routing); keeping the check
        // here too means a custom router that reuses this one still honours preferences.
        if (NotificationPreferenceRules.IsSuppressed(recipient.Preferences, priority, category))
            return NotificationChannel.None;

        return priority switch
        {
            // Critical: all registered channels
            NotificationPriority.Critical => CombineAll(registered),

            // High: user's preferred channel + email + push (if registered)
            NotificationPriority.High => CombinePreferred(registered,
                recipient.Preferences?.PreferredChannel ?? NotificationChannel.None,
                NotificationChannel.Email,
                NotificationChannel.Push),

            // Normal: recipient's resolved channel, or email fallback
            NotificationPriority.Normal => ResolvePreferred(recipient, registered),

            // Low: user's preferred channel if registered, otherwise email (digest candidate)
            NotificationPriority.Low => recipient.Preferences?.PreferredChannel is { } pref
                && pref != NotificationChannel.None
                && registered.Contains(pref)
                    ? pref
                    : registered.Contains(NotificationChannel.Email)
                        ? NotificationChannel.Email
                        : registered[0],

            _ => registered[0],
        };
    }

    private static NotificationChannel CombineAll(IReadOnlyList<NotificationChannel> channels)
    {
        var result = NotificationChannel.None;
        foreach (var c in channels)
            result |= c;
        return result;
    }

    private static NotificationChannel CombinePreferred(
        IReadOnlyList<NotificationChannel> registered,
        params NotificationChannel[] preferred)
    {
        var result = NotificationChannel.None;
        foreach (var p in preferred)
        {
            if (registered.Contains(p))
                result |= p;
        }

        // Fallback to first registered if none of the preferred are available
        return result == NotificationChannel.None ? registered[0] : result;
    }

    private static NotificationChannel ResolvePreferred(
        ResolvedRecipient recipient,
        IReadOnlyList<NotificationChannel> registered)
    {
        // Use the channel from resolution if it's registered
        if (registered.Contains(recipient.Channel))
            return recipient.Channel;

        // Prefer email, then first registered
        return registered.Contains(NotificationChannel.Email)
            ? NotificationChannel.Email
            : registered[0];
    }
}
