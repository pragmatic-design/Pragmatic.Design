using Microsoft.Extensions.DependencyInjection;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Actions.Boundary;

/// <summary>
///     Extension methods for registering bounded contexts.
/// </summary>
public static class BoundaryServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Registers a boundary configuration.
        /// </summary>
        /// <typeparam name="TBoundary">The boundary marker type.</typeparam>
        /// <param name="configuration">The boundary configuration.</param>
        /// <returns>The service collection for chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown when configuration is invalid.</exception>
        public IServiceCollection AddBoundary<TBoundary>(BoundaryConfiguration<TBoundary> configuration)
            where TBoundary : IBoundary
        {
            ThrowIfNull(services);
            ThrowIfNull(configuration);

            // A second registration would mean two singletons, and every reader takes the first, so a
            // later UseRemote meant to replace a UseLocal would do nothing and say nothing.
            // Refused here rather than by an opt-in validator, so the check holds for every application.
            if (services.HasBoundary<TBoundary>())
                throw new InvalidOperationException(
                    $"Boundary '{typeof(TBoundary).Name}' is already registered. Configure it with a single " +
                    $"AddBoundary<{typeof(TBoundary).Name}>(…) call.");

            configuration.Validate();

            // Register configuration as singleton for runtime lookup
            services.AddSingleton(configuration);

            // Also as the non-generic view GetAllBoundaryConfigurations enumerates.
            services.AddSingleton<IBoundaryConfiguration>(new BoundaryConfigurationAdapter<TBoundary>(configuration));

            if (configuration.Mode == BoundaryMode.Remote)
            {
                // Client name must match SG convention: Pragmatic.Remote.{ModuleName}
                var moduleName = typeof(TBoundary).Name;
                if (moduleName.EndsWith("Boundary", StringComparison.Ordinal))
                    moduleName = moduleName[..^"Boundary".Length];
                var clientName = $"Pragmatic.Remote.{moduleName}";

                var remoteBaseUrl = configuration.RemoteBaseUrl
                                    ?? throw new InvalidOperationException(
                                        $"Remote boundary '{typeof(TBoundary).Name}' has no base URL. Call UseRemote(baseUrl) before registering.");

                services.AddHttpClient(clientName,
                    client => { client.BaseAddress = new Uri(remoteBaseUrl); });
            }

            return services;
        }

        /// <summary>
        ///     Registers a boundary with inline configuration.
        /// </summary>
        /// <typeparam name="TBoundary">The boundary marker type.</typeparam>
        /// <param name="configure">The configuration action.</param>
        /// <returns>The service collection for chaining.</returns>
        /// <example>
        ///     <code>
        /// services.AddBoundary&lt;StudentsBoundary&gt;(cfg => cfg
        ///     .UseLocal()
        ///     .UseDatabase(opt => opt.UseNpgsql(connectionString)));
        /// </code>
        /// </example>
        public IServiceCollection AddBoundary<TBoundary>(Action<BoundaryConfiguration<TBoundary>> configure)
            where TBoundary : IBoundary
        {
            var configuration = new BoundaryConfiguration<TBoundary>();
            configure(configuration);
            return services.AddBoundary(configuration);
        }

        /// <summary>
        ///     Gets the configuration for a boundary type.
        /// </summary>
        /// <typeparam name="TBoundary">The boundary marker type.</typeparam>
        /// <returns>The boundary configuration, or null if not registered.</returns>
        public BoundaryConfiguration<TBoundary>? GetBoundaryConfiguration<TBoundary>()
            where TBoundary : IBoundary
        {
            var descriptor = services.FirstOrDefault(d =>
                d.ServiceType == typeof(BoundaryConfiguration<TBoundary>) &&
                d.ImplementationInstance is not null);

            return descriptor?.ImplementationInstance as BoundaryConfiguration<TBoundary>;
        }

        /// <summary>
        ///     Checks if a boundary is registered.
        /// </summary>
        /// <typeparam name="TBoundary">The boundary marker type.</typeparam>
        /// <returns>True if the boundary is registered, false otherwise.</returns>
        public bool HasBoundary<TBoundary>()
            where TBoundary : IBoundary
        {
            return services.Any(d => d.ServiceType == typeof(BoundaryConfiguration<TBoundary>));
        }

        /// <summary>
        ///     Gets all registered boundary configurations.
        /// </summary>
        /// <returns>All registered boundary configurations.</returns>
        public IEnumerable<IBoundaryConfiguration> GetAllBoundaryConfigurations()
        {
            return services
                .Where(d => d.ServiceType == typeof(IBoundaryConfiguration))
                .Select(d => d.ImplementationInstance)
                .OfType<IBoundaryConfiguration>();
        }
    }
}
