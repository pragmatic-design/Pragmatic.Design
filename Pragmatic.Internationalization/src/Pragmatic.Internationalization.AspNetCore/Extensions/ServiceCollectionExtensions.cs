using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Internationalization.AspNetCore.Json.Extensions;
using Pragmatic.Internationalization.AspNetCore.Middleware;
using Pragmatic.Internationalization.AspNetCore.Providers;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Formatting;

namespace Pragmatic.Internationalization.AspNetCore.Extensions;

/// <summary>
///     Extension methods for configuring Pragmatic.Internationalization in ASP.NET Core.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds Pragmatic.Internationalization services to the service collection.
        /// </summary>
        /// <param name="configure">Optional configuration action for I18N options.</param>
        /// <returns>An <see cref="I18NBuilder"/> for chaining additional configuration.</returns>
        /// <example>
        /// <code>
        /// // Simple configuration
        /// builder.Services.AddPragmaticInternationalization(options =>
        /// {
        ///     options.DefaultUICulture = CultureCode.Italian;
        ///     options.SupportedCultures = [CultureCode.Italian, CultureCode.English];
        ///     options.SyncScopes = true;
        /// });
        /// 
        /// // With additional providers
        /// builder.Services.AddPragmaticInternationalization(options =>
        /// {
        ///     options.DefaultUICulture = CultureCode.EnglishUS;
        /// })
        /// .AddConfigProvider&lt;TenantConfigProvider&gt;()
        /// .AddConfigProvider&lt;UserConfigProvider&gt;();
        /// </code>
        /// </example>
        public I18NBuilder AddPragmaticInternationalization(Action<I18NOptions>? configure = null)
        {
            Ensure.Ensure.ThrowIfNull(services);

            // Configure I18NOptions
            var optionsBuilder = services.AddOptions<I18NOptions>();
            if (configure is not null)
            {
                optionsBuilder.Configure(configure);
            }

            // Register SystemConfigProvider (always present, priority 0)
            services.AddSingleton<II18NConfigProvider, SystemConfigProvider>();

            // Register the resolver (scoped to allow per-request provider resolution)
            services.AddScoped<I18NConfigResolver>();

            // The one question a module asks of the configuration — "what language when nobody said?" —
            // as a contract a module may declare a dependency on. The resolver itself is not one: it
            // carries the provider merge and the validation, and a module that injected it was refused
            // with PRAG1641 although the application resolves it, so a module wrote a constant instead.
            // Scoped, like the resolver, because a provider may be per-request.
            services.AddScoped<IConfiguredCultures, ConfiguredCultures>();

            // The contract application code is meant to depend on. It lives in Pragmatic.Abstractions and
            // I18NContext implements it, but nothing registered it — so no consumer could inject it, and
            // the GlobalizationFormatter constructor that takes one was unreachable. Same timing as the
            // formatter below: resolved inside the request scope, after the middleware set the context.
            services.AddScoped<IGlobalizationContext>(_ => I18NContext.Current);

            // Register GlobalizationFormatter as scoped so it is created once per request.
            // The factory reads I18NContext.Current at first-resolution time (inside the request scope),
            // after the middleware has already set the context. Using AddScoped instead of AddTransient
            // ensures consistent culture throughout a single request.
            services.AddScoped(sp => new GlobalizationFormatter(sp.GetRequiredService<IGlobalizationContext>()));

            // Configure JSON serialization
            services.ConfigureHttpJsonOptions(jsonOptions =>
            {
                jsonOptions.SerializerOptions.AddPragmaticInternationalization();
            });

            return new I18NBuilder(services);
        }

        /// <summary>
        ///     Adds Pragmatic.Internationalization services and binds configuration from a section.
        /// </summary>
        /// <param name="configuration">The configuration to bind from.</param>
        /// <param name="sectionName">The section name (default: "I18N").</param>
        /// <returns>An <see cref="I18NBuilder"/> for chaining additional configuration.</returns>
        /// <example>
        /// <code>
        /// // appsettings.json:
        /// // {
        /// //   "I18N": {
        /// //     "DefaultUICulture": "it-IT",
        /// //     "SupportedCultures": ["it-IT", "en-US"]
        /// //   }
        /// // }
        /// 
        /// builder.Services.AddPragmaticInternationalization(builder.Configuration);
        /// </code>
        /// </example>
        public I18NBuilder AddPragmaticInternationalization(IConfiguration configuration,
            string sectionName = I18NOptions.SectionName)
        {
            Ensure.Ensure.ThrowIfNull(services);
            Ensure.Ensure.ThrowIfNull(configuration);

            services.Configure<I18NOptions>(configuration.GetSection(sectionName));

            return AddPragmaticInternationalization(services);
        }

        /// <summary>
        ///     Registers a localization provider as one more source of translations — the generated host's
        ///     call for the provider a module generates over its embedded translations.
        /// </summary>
        /// <remarks>
        ///     The same registration as <see cref="I18NBuilder.AddLocalizationProvider{TProvider}()" />, for a
        ///     caller that holds the service collection rather than the builder: it keeps the gateway the
        ///     interface resolves to registered last.
        /// </remarks>
        /// <typeparam name="TProvider">The provider type.</typeparam>
        public IServiceCollection AddPragmaticLocalizationProvider<
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
                System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>()
            where TProvider : class, global::Pragmatic.Internationalization.Providers.ILocalizationProvider
        {
            Ensure.Ensure.ThrowIfNull(services);

            new I18NBuilder(services).AddLocalizationProvider<TProvider>();
            return services;
        }
    }

    /// <summary>
    ///     Adds the Pragmatic.Internationalization middleware to the application pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder for chaining.</returns>
    /// <example>
    /// <code>
    /// app.UsePragmaticInternationalization();
    /// app.UseRouting();
    /// // ... other middleware
    /// </code>
    /// </example>
    /// <exception cref="I18NConfigurationException">
    ///     No default UI culture is configured and none can come from a provider that answers per request.
    /// </exception>
    public static IApplicationBuilder UsePragmaticInternationalization(this IApplicationBuilder app)
    {
        Ensure.Ensure.ThrowIfNull(app);

        // Here, while the pipeline is built: a configuration that cannot resolve fails the start, not
        // every request after it.
        I18NStartupCheck.Verify(app.ApplicationServices);
        return app.UseMiddleware<I18NContextMiddleware>();
    }
}
