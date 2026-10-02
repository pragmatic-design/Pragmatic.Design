using System.Collections.Frozen;

namespace Pragmatic.Notifications.Channels;

/// <summary>
///     DI-based channel factory. Resolves channels from registered <see cref="INotificationChannel"/> implementations.
/// </summary>
internal sealed class DefaultChannelFactory : INotificationChannelFactory
{
    private readonly FrozenDictionary<NotificationChannel, INotificationChannel> _channels;
    private readonly NotificationChannel[] _registered;

    public DefaultChannelFactory(IEnumerable<INotificationChannel> channels)
    {
        // Last registration wins per channel: throwing "duplicate key" at startup when two providers
        // are registered for the same channel would be a hostile way to report a configuration override.
        var byChannel = new Dictionary<NotificationChannel, INotificationChannel>();
        foreach (var channel in channels)
            byChannel[channel.Channel] = channel;

        _channels = byChannel.ToFrozenDictionary();

        // Ordered deterministically. Routing falls back to "the first registered channel", and taking
        // that from a hash-ordered key collection made the fallback vary between runs — a low-priority
        // notification could land on SMS instead of e-mail depending on hashing.
        _registered = [.. byChannel.Keys.Order()];
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <paramref name="tenantId"/> is accepted for per-tenant channel selection but is not used by
    ///     this implementation — every tenant shares the registered channels. Supply a custom
    ///     <see cref="INotificationChannelFactory"/> to vary channels per tenant.
    /// </remarks>
    public INotificationChannel? GetChannel(NotificationChannel channel, string? tenantId = null)
        => _channels.GetValueOrDefault(channel);

    public IReadOnlyList<NotificationChannel> GetRegisteredChannels() => _registered;
}
