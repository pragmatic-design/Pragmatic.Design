using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Resilience.Configuration;
using Pragmatic.Resilience.State;

namespace Pragmatic.Resilience;

/// <summary>
/// Extension methods for registering resilience services.
/// </summary>
public static class ResilienceServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds Pragmatic.Resilience services to the DI container.
        /// Registers IResiliencePipelineProvider and default state store.
        /// </summary>
        public IServiceCollection AddPragmaticResilience(Action<ResilienceOptions>? configure = null)
        {
            // Ensure IOptions<ResilienceOptions> is registered even when no configure action is supplied —
            // ResiliencePipelineProvider depends on it. Configure<T> calls AddOptions<T> internally, so it's
            // only needed in the unconfigured path.
            if (configure is not null)
                services.Configure(configure);
            else
                services.AddOptions<ResilienceOptions>();

            // Default in-memory state store (can be replaced by Redis/DB provider)
            services.TryAddSingleton<ICircuitBreakerStateStore, InMemoryCircuitBreakerStateStore>();

            // Pipeline provider — register as concrete then expose both interfaces to avoid double-registration.
            services.TryAddSingleton<ResiliencePipelineProvider>();
            services.TryAddSingleton<IResiliencePipelineProvider>(sp => sp.GetRequiredService<ResiliencePipelineProvider>());
            services.TryAddSingleton<IResiliencePipelineRegistry>(sp => sp.GetRequiredService<ResiliencePipelineProvider>());

            return services;
        }

        /// <summary>
        /// Adds Pragmatic.Resilience services from IConfiguration binding.
        /// Binds the "Resilience" section to ResilienceOptions.
        /// </summary>
        public IServiceCollection AddPragmaticResilience(IConfiguration configuration)
        {
            var section = configuration.GetSection("Resilience");
            services.Configure<ResilienceOptions>(options =>
            {
                section.Bind(options);
            });

            // Default in-memory state store
            services.TryAddSingleton<ICircuitBreakerStateStore, InMemoryCircuitBreakerStateStore>();

            // Pipeline provider — register as concrete then expose both interfaces.
            services.TryAddSingleton<ResiliencePipelineProvider>();
            services.TryAddSingleton<IResiliencePipelineProvider>(sp => sp.GetRequiredService<ResiliencePipelineProvider>());
            services.TryAddSingleton<IResiliencePipelineRegistry>(sp => sp.GetRequiredService<ResiliencePipelineProvider>());

            return services;
        }

        /// <summary>
        /// Adds a named resilience policy with fluent configuration.
        /// </summary>
        public IServiceCollection AddResiliencePolicy(string name,
            Action<ResiliencePolicyOptions> configure)
        {
            services.Configure<ResilienceOptions>(options =>
            {
                var policyOptions = new ResiliencePolicyOptions();
                configure(policyOptions);
                options.Policies[name] = policyOptions;
            });

            return services;
        }
    }
}
