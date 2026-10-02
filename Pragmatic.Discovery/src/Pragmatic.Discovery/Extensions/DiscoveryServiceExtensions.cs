// Pragmatic.Discovery - Service Extensions

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Discovery.Abstractions;
using Pragmatic.Discovery.InMemory;
using Pragmatic.Discovery.Options;
using Pragmatic.Discovery.Services;
using Pragmatic.Discovery.Startup;

namespace Pragmatic.Discovery.Extensions;

/// <summary>
/// Extension methods for registering Pragmatic Discovery services.
/// </summary>
public static class DiscoveryServiceExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the Discovery service with the InMemory backend (default).
        /// The hosted service will auto-register this host's topology on startup.
        /// </summary>
        /// <example>
        /// <code>
        /// // Minimal — uses InMemory backend, auto-registers, validates on startup
        /// services.AddDiscovery();
        ///
        /// // With configuration override
        /// services.AddDiscovery(opts =>
        /// {
        ///     opts.ThrowOnValidationFailure = true;
        ///     opts.ValidateOnStartup = true;
        /// });
        /// </code>
        /// </example>
        public IServiceCollection AddDiscovery(Action<DiscoveryOptions>? configure = null)
        {
            // Options
            var optionsBuilder = services.AddOptions<DiscoveryOptions>();
            if (configure is not null)
                optionsBuilder.Configure(configure);

            // Default backend: InMemory (singleton — shared across all requests in the process)
            services.TryAddSingleton<IDiscoveryBackend, InMemoryDiscoveryBackend>();

            // Core services
            services.TryAddSingleton<IDiscoveryService, DiscoveryService>();

            // Auto-registration at startup
            services.AddHostedService<DiscoveryHostedService>();

            return services;
        }

        /// <summary>
        /// Replaces the default InMemory backend with a custom one.
        /// Must be called after <see cref="AddDiscovery"/>; throws <see cref="InvalidOperationException"/> otherwise.
        /// </summary>
        /// <typeparam name="TBackend">Custom backend implementing <see cref="IDiscoveryBackend"/>.</typeparam>
        /// <exception cref="InvalidOperationException">Thrown when <see cref="AddDiscovery"/> has not been called first.</exception>
        public IServiceCollection UseDiscoveryBackend<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TBackend>()
            where TBackend : class, IDiscoveryBackend
        {
            var hasDiscovery = services.Any(d => d.ServiceType == typeof(IDiscoveryService));
            if (!hasDiscovery)
                throw new InvalidOperationException(
                    $"{nameof(UseDiscoveryBackend)} requires {nameof(AddDiscovery)} to be called first. " +
                    "Call services.AddDiscovery() before calling UseDiscoveryBackend<T>().");

            // Remove the default InMemory registration and replace
            var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDiscoveryBackend));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddSingleton<IDiscoveryBackend, TBackend>();
            return services;
        }
    }
}
