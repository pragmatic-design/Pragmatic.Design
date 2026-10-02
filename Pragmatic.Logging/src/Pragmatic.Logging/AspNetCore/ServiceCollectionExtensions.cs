using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Logging.Context;

namespace Pragmatic.Logging.AspNetCore;

/// <summary>
/// Extension methods for configuring Pragmatic.Logging with ASP.NET Core.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <param name="services">The service collection</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds Pragmatic.Logging ASP.NET Core integration to the service collection.
        /// </summary>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticHttpLogging()
        {
            // Ensure HttpContextAccessor is registered
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            // Register context providers
            RegisterContextProviders(services);

            return services;
        }

        /// <summary>
        /// Adds Pragmatic.Logging ASP.NET Core integration with custom configuration.
        /// </summary>
        /// <param name="configure">Configuration action</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticHttpLogging(Action<HttpContextEnrichmentOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            var options = new HttpContextEnrichmentOptions();
            configure(options);

            services.AddSingleton(options);

            return services.AddPragmaticHttpLogging();
        }

        /// <summary>
        /// Adds HTTP context enrichment to Pragmatic.Logging.
        /// </summary>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddHttpContextEnrichment()
        {
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            // Register as singleton to be added to ContextManager
            services.AddSingleton<IContextProvider, HttpContextProvider>();
            services.AddSingleton<IContextProvider, CorrelationIdProvider>();

            return services;
        }

        /// <summary>
        /// Configures correlation ID tracking.
        /// </summary>
        /// <param name="headerName">Custom header name for correlation ID</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddCorrelationIdTracking(string? headerName = null)
        {
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            // Wire the configured header name through to the provider so the custom header is
            // actually used to read the inbound correlation id and to write the response header.
            services.AddSingleton<IContextProvider>(sp =>
                new CorrelationIdProvider(
                    sp.GetRequiredService<IHttpContextAccessor>(),
                    headerName));

            return services;
        }
    }

    private static void RegisterContextProviders(IServiceCollection services)
    {
        // Register HTTP context providers
        services.AddSingleton<IContextProvider, HttpContextProvider>();
        services.AddSingleton<IContextProvider, CorrelationIdProvider>();

        // Register a hosted service that adds all IContextProvider instances to ContextManager
        services.AddHostedService<ContextProviderRegistrationService>();
    }
}
