using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Pragmatic.Notifications.Configuration;

namespace Pragmatic.Notifications.Delivery;

/// <summary>
///     BoundedChannel wrapper for async notification delivery. Used by EnqueueAsync.
///     Capacity is driven from <see cref="NotificationOptions.DeliveryChannelCapacity"/>.
/// </summary>
internal sealed class NotificationDeliveryChannel
{
    private readonly Channel<QueuedNotification> _channel;

    public NotificationDeliveryChannel(IOptions<NotificationOptions> options)
    {
        var capacity = options.Value.DeliveryChannelCapacity;
        _channel = Channel.CreateBounded<QueuedNotification>(new BoundedChannelOptions(capacity)
        {
            // Wait (back-pressure on EnqueueAsync) — DropOldest would silently discard queued
            // notifications and leave their Pending tracking records unresolved forever.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    public ChannelWriter<QueuedNotification> Writer => _channel.Writer;
    public ChannelReader<QueuedNotification> Reader => _channel.Reader;
}
