using Microsoft.AspNetCore.Builder;

namespace Pragmatic.Logging.AspNetCore;

/// <summary>
/// Extension methods for configuring Pragmatic.Logging middleware.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <param name="app">The application builder</param>
    extension(IApplicationBuilder app)
    {
        /// <summary>
        /// Adds Pragmatic.Logging enrichment middleware to the pipeline.
        /// </summary>
        /// <returns>The application builder for chaining</returns>
        public IApplicationBuilder UsePragmaticLogging()
        {
            return app.UseMiddleware<LoggingEnrichmentMiddleware>();
        }

        /// <summary>
        /// Adds Pragmatic.Logging enrichment middleware with custom options.
        /// </summary>
        /// <param name="options">Middleware options</param>
        /// <returns>The application builder for chaining</returns>
        public IApplicationBuilder UsePragmaticLogging(LoggingEnrichmentOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            return app.UseMiddleware<LoggingEnrichmentMiddleware>(options);
        }

        /// <summary>
        /// Adds Pragmatic.Logging enrichment middleware with configuration.
        /// </summary>
        /// <param name="configure">Configuration action</param>
        /// <returns>The application builder for chaining</returns>
        public IApplicationBuilder UsePragmaticLogging(Action<LoggingEnrichmentOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            var options = new LoggingEnrichmentOptions();
            configure(options);

            return app.UseMiddleware<LoggingEnrichmentMiddleware>(options);
        }

        /// <summary>
        /// Adds W3C Baggage propagation middleware. Propagates user ID, tenant ID,
        /// and custom values into <c>Activity.Baggage</c> for cross-service context forwarding.
        /// </summary>
        /// <returns>The application builder for chaining</returns>
        public IApplicationBuilder UsePragmaticBaggage()
        {
            return app.UseMiddleware<BaggagePropagationMiddleware>();
        }

        /// <summary>
        /// Adds W3C Baggage propagation middleware with custom options.
        /// </summary>
        /// <param name="options">Baggage propagation options</param>
        /// <returns>The application builder for chaining</returns>
        public IApplicationBuilder UsePragmaticBaggage(BaggagePropagationOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return app.UseMiddleware<BaggagePropagationMiddleware>(options);
        }

        /// <summary>
        /// Adds W3C Baggage propagation middleware with configuration.
        /// </summary>
        /// <param name="configure">Configuration action</param>
        /// <returns>The application builder for chaining</returns>
        public IApplicationBuilder UsePragmaticBaggage(Action<BaggagePropagationOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            var options = new BaggagePropagationOptions();
            configure(options);

            return app.UseMiddleware<BaggagePropagationMiddleware>(options);
        }
    }
}