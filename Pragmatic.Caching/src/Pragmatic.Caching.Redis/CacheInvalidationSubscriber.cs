using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Caching.Diagnostics;
using Pragmatic.Telemetry;
using Pragmatic.Telemetry.Conventions;
using StackExchange.Redis;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Caching.Redis;

/// <summary>
///     Applies the invalidations other nodes broadcast to this node's <see cref="HybridCache" />.
/// </summary>
/// <remarks>
///     <para>
///         Applied to <see cref="HybridCache" /> and not to <see cref="ICacheStack" />, on purpose: the
///         stack is decorated with <see cref="BroadcastingCacheStack" />, and an invalidation applied
///         through it would be broadcast again — by every node, to every node, forever. Beneath the
///         decorator it is removed here and nowhere else.
///     </para>
///     <para>
///         A node's own messages come back to it on the same channel and are ignored: the invalidation
///         they describe already ran here, before it was published.
///     </para>
///     <para>
///         Each message applied is an activity, <see cref="ActivityName" />, carrying the node that
///         applied it (<see cref="CacheTags.Node" />) — which is also how a test knows the other node has
///         dropped the entry without sleeping on it.
///     </para>
/// </remarks>
public sealed partial class CacheInvalidationSubscriber : IHostedService
{
    /// <summary>The activity each applied message produces.</summary>
    public const string ActivityName = "Cache.RemoteInvalidation";

    private readonly RedisCacheInvalidationChannel _channel;
    private readonly HybridCache _cache;
    private readonly ILogger<CacheInvalidationSubscriber> _logger;
    private ChannelMessageQueue? _subscription;

    /// <summary>Creates the subscriber for this node.</summary>
    public CacheInvalidationSubscriber(
        RedisCacheInvalidationChannel channel,
        HybridCache cache,
        ILogger<CacheInvalidationSubscriber> logger)
    {
        ThrowIfNull(channel);
        ThrowIfNull(cache);
        ThrowIfNull(logger);
        _channel = channel;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
        => _subscription = await _channel.SubscribeAsync(ApplyAsync).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscription is not null)
            await _subscription.UnsubscribeAsync().ConfigureAwait(false);
    }

    /// <summary>Applies one message, unless this node sent it.</summary>
    public async Task ApplyAsync(CacheInvalidationMessage message)
    {
        ThrowIfNull(message);

        if (message.Origin == _channel.NodeId)
            return;

        using var activity = CachingDiagnostics.ActivitySource.StartActivity(ActivityName);
        activity?.SetTag(CacheTags.Operation, "invalidate");
        activity?.SetTag(CacheTags.Node, _channel.NodeId);
        activity?.SetTag(message.Kind == CacheInvalidationKind.Tag ? CacheTags.Tags : CacheTags.Key, message.Value);

        if (message.Kind == CacheInvalidationKind.Tag)
            await _cache.RemoveByTagAsync(message.Value).ConfigureAwait(false);
        else
            await _cache.RemoveAsync(message.Value).ConfigureAwait(false);

        CachingDiagnostics.CacheInvalidations.Add(1);
        LogApplied(message.Kind, message.Value, message.Origin);
        activity?.SetSuccess();
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Applied the invalidation of {Kind} '{Value}' broadcast by node {Origin}")]
    private partial void LogApplied(CacheInvalidationKind kind, string value, string origin);
}
