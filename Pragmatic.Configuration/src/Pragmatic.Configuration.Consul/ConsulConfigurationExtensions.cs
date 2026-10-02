using global::Consul;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Configuration.Extensions;

namespace Pragmatic.Configuration.Consul;

/// <summary>DI registration for the HashiCorp Consul KV configuration backend.</summary>
public static class ConsulConfigurationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds a Consul KV-backed configuration store. Replaces the default in-memory configuration store
        ///     and re-applies read-through caching when enabled.
        /// </summary>
        public IServiceCollection AddConsulConfigurationStore(Action<ConsulConfigurationOptions> configure)
        {
            var options = new ConsulConfigurationOptions();
            configure(options);
            services.AddSingleton(options);

            services.TryAddSingleton<IConsulClient>(_ => new ConsulClient(c =>
            {
                c.Address = new Uri(options.Address);
                if (!string.IsNullOrEmpty(options.Token))
                    c.Token = options.Token;
            }));

            services.AddSingleton<IConfigurationStore>(sp =>
                new ConsulConfigurationStore(sp.GetRequiredService<IConsulClient>(), options));

            services.DecorateConfigurationStoreWithCaching();
            return services;
        }
    }
}
