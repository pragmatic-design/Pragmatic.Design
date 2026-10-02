using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Composition.Extensions;
using StackExchange.Redis;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Caching.Redis;

/// <summary>
///     Registration for the invalidation broadcast: what one node invalidates, every node drops.
/// </summary>
public static class RedisCacheInvalidationExtensions
{
    /// <summary>
    ///     Broadcasts every invalidation of the registered <see cref="ICacheStack" /> over Redis pub/sub,
    ///     and applies the other nodes' to this one, using an already-registered
    ///     <see cref="IConnectionMultiplexer" />. Call AFTER <c>AddPragmaticCaching()</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Needed as soon as there is more than one host. A distributed cache behind
    ///         <c>HybridCache</c> shares the entries; it does not share the invalidations, and each host's
    ///         in-process copy outlives an invalidation run anywhere else.
    ///     </para>
    ///     <para>
    ///         A single host does not need it, and without this call nothing changes: the stack does not
    ///         look for a broadcast at runtime, the host declares one.
    ///     </para>
    /// </remarks>
    public static IServiceCollection AddRedisCacheInvalidationBroadcast(
        this IServiceCollection services,
        Action<RedisCacheInvalidationOptions>? configure = null)
    {
        ThrowIfNull(services);

        var options = services.AddOptions<RedisCacheInvalidationOptions>();
        if (configure is not null)
            options.Configure(configure);

        services.TryAddSingleton<RedisCacheInvalidationChannel>();

        // The default stack only. A category stack is a prefix over it, resolved from the container,
        // so its invalidations already reach this decorator — with the prefix on, which is the key
        // HybridCache holds. Decorating the categories as well would publish every tag twice and every
        // key once without its prefix.
        services.DecorateUnkeyed<ICacheStack>((inner, sp) =>
            new BroadcastingCacheStack(inner, sp.GetRequiredService<RedisCacheInvalidationChannel>()));
        services.AddHostedService<CacheInvalidationSubscriber>();

        return services;
    }

    /// <summary>
    ///     The same, registering a singleton <see cref="IConnectionMultiplexer" /> for
    ///     <paramref name="connectionString" /> when none is registered yet. Call AFTER
    ///     <c>AddPragmaticCaching()</c>.
    /// </summary>
    public static IServiceCollection AddRedisCacheInvalidationBroadcast(
        this IServiceCollection services,
        string connectionString,
        Action<RedisCacheInvalidationOptions>? configure = null)
    {
        ThrowIfNull(services);
        ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingletonMultiplexer(connectionString);
        return services.AddRedisCacheInvalidationBroadcast(configure);
    }
}
