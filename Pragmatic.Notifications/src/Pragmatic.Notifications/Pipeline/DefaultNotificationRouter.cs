using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Preferences;

namespace Pragmatic.Notifications.Pipeline;

/// <summary>
///     Default routing strategy: maps priority to channels based on preferences and registered channels, among the channels
///     the recipient's address belongs to.
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

        // Priority chooses among the channels this address can be delivered on, never beyond them: the resolver says which
        // channels an address belongs to (an e-mail address Email, a phone number Sms; a flags value when one address serves
        // several, Push included), as ChannelOverride already does. Handing every registered channel to every recipient sent
        // an e-mail address to the SMS provider and the webhook. A notification reaches several channels through several
        // addresses, one resolved recipient each.
        var reachable = recipient.Channel & CombineAll(registered);
        if (reachable == NotificationChannel.None)
            return NotificationChannel.None;

        var preferred = recipient.Preferences?.PreferredChannel ?? NotificationChannel.None;
        return priority switch
        {
            // Critical: every channel the address reaches.
            NotificationPriority.Critical => reachable,

            // High: the user's preferred channel + email + push, where the address reaches them; otherwise every one it reaches.
            NotificationPriority.High => Narrow(reachable, preferred | NotificationChannel.Email | NotificationChannel.Push),

            // Low: the user's preferred channel, else email (digest candidate), else the first the address reaches.
            NotificationPriority.Low => FirstOf(reachable, preferred, NotificationChannel.Email),

            // Normal: the channels the address reaches.
            _ => reachable,
        };
    }

    private static NotificationChannel CombineAll(IReadOnlyList<NotificationChannel> channels)
    {
        var result = NotificationChannel.None;
        foreach (var c in channels)
            result |= c;
        return result;
    }

    /// <summary>The wanted channels the address reaches, or every one it reaches when it reaches none of them.</summary>
    private static NotificationChannel Narrow(NotificationChannel reachable, NotificationChannel wanted)
        => (reachable & wanted) is var narrowed and not NotificationChannel.None ? narrowed : reachable;

    /// <summary>The first single channel of <paramref name="candidates" /> the address reaches, else the lowest one it reaches.</summary>
    private static NotificationChannel FirstOf(NotificationChannel reachable, params NotificationChannel[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var single = Lowest(reachable & candidate);
            if (single != NotificationChannel.None)
                return single;
        }

        return Lowest(reachable);
    }

    private static NotificationChannel Lowest(NotificationChannel flags) => flags & (NotificationChannel)(-(int)flags);
}
