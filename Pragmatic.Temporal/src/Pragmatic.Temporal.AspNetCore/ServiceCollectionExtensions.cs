using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pragmatic.Temporal.AspNetCore.Middleware;
using Pragmatic.Temporal.AspNetCore.ModelBinding;
using Pragmatic.Temporal.Context;
using Pragmatic.Temporal.Extensions;
using Pragmatic.Serialization;
using Pragmatic.Temporal.Json;
using Pragmatic.Temporal.Json.Behaviors;

namespace Pragmatic.Temporal.AspNetCore;

/// <summary>
///     Extension methods for registering Pragmatic.Temporal ASP.NET Core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Adds Pragmatic.Temporal ASP.NET Core services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureTemporal">Configuration for core temporal options.</param>
    /// <param name="configureAspNetCore">Configuration for ASP.NET Core specific options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPragmaticTemporalAspNetCore(
        this IServiceCollection services,
        Action<TemporalOptions>? configureTemporal = null,
        Action<TemporalAspNetCoreOptions>? configureAspNetCore = null)
    {
        // Add core services
        services.AddPragmaticTemporal(configureTemporal);

        // Add ASP.NET Core options, propagating configured values to detection strategies
        var aspOptions = new TemporalAspNetCoreOptions();
        configureAspNetCore?.Invoke(aspOptions);
        aspOptions.PropagateOptionsToStrategies();
        services.TryAddSingleton(Options.Create(aspOptions));

        // Add HTTP context accessor if not already registered
        services.AddHttpContextAccessor();

        // Add temporal context accessor
        services.TryAddScoped<TemporalContextAccessor>();

        // Override TemporalContext to come from HTTP context
        services.AddScoped(sp => sp.GetRequiredService<TemporalContextAccessor>().Context);

        // Configure JSON for MVC controllers: temporal converters + per-property
        // timezone behaviors (attribute-driven, resolved via the behavior registry).
        services.Configure<JsonOptions>(options =>
        {
            options.JsonSerializerOptions.AddPragmaticTemporal();
        });

        // Same for Minimal APIs, which read Microsoft.AspNetCore.Http.Json.JsonOptions.
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.AddPragmaticTemporal();
        });

        // ⚠️ The per-property timezone behaviours are a modifier, and a modifier goes on the shared
        // seam — not on whichever JsonSerializerOptions this method is handed. Wrapping the options
        // here worked only until something assigned a resolver afterwards, which the generated host
        // does; the wrap was then discarded and nothing said so, because a modifier that does not run
        // produces a well-formed payload with the wrong values.
        services.AddPragmaticJsonModifier(TemporalJsonModifier.Apply);

        // Add model binder providers: temporal types first, then attribute-driven
        // DateTimeOffset/DateTime conversion (both must precede the MVC defaults).
        services.Configure<MvcOptions>(options =>
        {
            options.ModelBinderProviders.Insert(0, new TemporalModelBinderProvider());
            options.ModelBinderProviders.Insert(1, new TemporalConversionModelBinderProvider());
        });

        return services;
    }

    /// <summary>
    ///     Adds the temporal context middleware to the pipeline.
    /// </summary>
    public static IApplicationBuilder UsePragmaticTemporal(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TemporalContextMiddleware>();
    }
}
