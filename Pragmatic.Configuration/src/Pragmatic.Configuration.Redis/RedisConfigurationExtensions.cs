using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Configuration.Extensions;
using StackExchange.Redis;

namespace Pragmatic.Configuration.Redis;

/// <summary>DI registration for the Redis configuration backend.</summary>
public static class RedisConfigurationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds a Redis-backed configuration store. Replaces the default in-memory configuration store and
        ///     re-applies read-through caching when enabled. Reuses a host-registered
        ///     <see cref="IConnectionMultiplexer" /> if present; otherwise connects using
        ///     <see cref="RedisConfigurationOptions.Configuration" />.
        /// </summary>
        public IServiceCollection AddRedisConfigurationStore(Action<RedisConfigurationOptions> configure)
        {
            var options = new RedisConfigurationOptions();
            configure(options);
            services.AddSingleton(options);

            services.TryAddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(options.Configuration));

            services.AddSingleton<IConfigurationStore>(sp =>
                new RedisConfigurationStore(sp.GetRequiredService<IConnectionMultiplexer>(), options));

            services.DecorateConfigurationStoreWithCaching();
            return services;
        }
    }
}
