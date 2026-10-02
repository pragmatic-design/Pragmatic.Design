using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Extensions;
using StackExchange.Redis;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Caching.Redis;

/// <summary>
///     Registration for Redis-backed atomic counters: decorates the registered
///     <see cref="ICacheStack"/> with <see cref="RedisCounterCacheStack"/> so
///     <c>IncrementAsync</c> (rate limiting, quotas) is atomic across instances.
/// </summary>
public static class RedisCounterCachingExtensions
{
    /// <summary>
    ///     Routes <see cref="ICacheStack.IncrementAsync"/> to Redis using an already-registered
    ///     <see cref="IConnectionMultiplexer"/>. Call AFTER <c>AddPragmaticCaching()</c>.
    /// </summary>
    public static IServiceCollection AddRedisAtomicCounters(this IServiceCollection services)
    {
        ThrowIfNull(services);
        return services.Decorate<ICacheStack>((inner, sp) =>
            new RedisCounterCacheStack(inner, sp.GetRequiredService<IConnectionMultiplexer>()));
    }

    /// <summary>
    ///     Routes <see cref="ICacheStack.IncrementAsync"/> to Redis, registering a singleton
    ///     <see cref="IConnectionMultiplexer"/> for <paramref name="connectionString"/> when none
    ///     is registered yet. Call AFTER <c>AddPragmaticCaching()</c>.
    /// </summary>
    public static IServiceCollection AddRedisAtomicCounters(
        this IServiceCollection services, string connectionString)
    {
        ThrowIfNull(services);
        ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingletonMultiplexer(connectionString);
        return services.AddRedisAtomicCounters();
    }

    /// <summary>One multiplexer per application, shared by the counters and the invalidation broadcast.</summary>
    internal static void TryAddSingletonMultiplexer(this IServiceCollection services, string connectionString)
        => Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
            .TryAddSingleton<IConnectionMultiplexer>(services,
                _ => ConnectionMultiplexer.Connect(connectionString));
}
