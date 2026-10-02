using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Caching.Redis;

/// <summary>
///     The Redis pub/sub channel the nodes of one application tell each other about invalidations on.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why it exists.</b> <c>HybridCache</c> keeps every entry in the process (L1) and, with a
///         distributed cache behind it, in that too (L2). An invalidation on one node reaches its own
///         L1 and the shared L2 — and no other node's L1, which keeps serving the old entry for its
///         whole local lifetime. Measured with two hosts on one Redis: the node that did
///         not run the invalidation answered the old list, while a node started afterwards read the
///         new one from the database.
///     </para>
///     <para>
///         <b>At most once.</b> Pub/sub delivers to whoever is subscribed when the message is published
///         and keeps nothing. A node that misses a message — disconnected, restarting — is stale for at
///         most the entry's local lifetime, which is exactly what every node was before this existed.
///         A failed publish is logged and does not fail the caller: the local invalidation has already
///         happened, and refusing the write that caused it would trade a stale read somewhere else for a
///         failed request here.
///     </para>
/// </remarks>
public sealed partial class RedisCacheInvalidationChannel
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisChannel _channel;
    private readonly ILogger<RedisCacheInvalidationChannel> _logger;

    /// <summary>Creates the channel for this node.</summary>
    public RedisCacheInvalidationChannel(
        IConnectionMultiplexer redis,
        IOptions<RedisCacheInvalidationOptions> options,
        ILogger<RedisCacheInvalidationChannel> logger)
    {
        ThrowIfNull(redis);
        ThrowIfNull(options);
        ThrowIfNull(logger);
        ThrowIfNullOrWhiteSpace(options.Value.Channel);

        _redis = redis;
        _channel = RedisChannel.Literal(options.Value.Channel);
        _logger = logger;
    }

    /// <summary>
    ///     This node, as it signs its messages. One per channel instance, and so one per host process
    ///     — two hosts in one test process are two nodes.
    /// </summary>
    public string NodeId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Tells the other nodes to drop what this one just dropped.</summary>
    public async ValueTask PublishAsync(CacheInvalidationKind kind, string value)
    {
        ThrowIfNullOrWhiteSpace(value);

        var message = new CacheInvalidationMessage(kind, value, NodeId);
        try
        {
            await _redis.GetSubscriber().PublishAsync(_channel, message.ToWire()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogPublishFailed(kind, value, ex);
        }
    }

    /// <summary>
    ///     Delivers every message published on the channel — this node's own included; telling them
    ///     apart is the receiver's job, because only it knows what "applying" means.
    /// </summary>
    /// <returns>The subscription, which the caller unsubscribes when it stops.</returns>
    public async Task<ChannelMessageQueue> SubscribeAsync(Func<CacheInvalidationMessage, Task> onMessage)
    {
        ThrowIfNull(onMessage);

        var queue = await _redis.GetSubscriber().SubscribeAsync(_channel).ConfigureAwait(false);
        queue.OnMessage(async delivered =>
        {
            var message = CacheInvalidationMessage.FromWire(delivered.Message);
            if (message is null)
            {
                LogUnreadableMessage(delivered.Message.ToString());
                return;
            }

            await onMessage(message).ConfigureAwait(false);
        });

        return queue;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Could not broadcast the invalidation of {Kind} '{Value}'; other nodes keep their copy until it expires")]
    private partial void LogPublishFailed(CacheInvalidationKind kind, string value, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Ignored a message on the cache invalidation channel that is not one: '{Message}'")]
    private partial void LogUnreadableMessage(string message);
}
