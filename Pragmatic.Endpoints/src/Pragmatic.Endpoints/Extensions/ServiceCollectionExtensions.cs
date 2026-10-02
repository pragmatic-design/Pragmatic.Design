using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Endpoints.Configuration;

namespace Pragmatic.Endpoints.Extensions;

/// <summary>
///     Extension methods for registering Pragmatic.Endpoints services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Adds Pragmatic.Endpoints services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    ///     <code>
    /// builder.Services.AddPragmaticEndpoints(options =>
    /// {
    ///     options.RoutePrefix = "/api";
    ///     options.EnableOpenApi = true;
    ///     options.ConfigureGroup("Orders", g =>
    ///     {
    ///         g.RoutePrefix = "/orders";
    ///         g.RequireAuthorization = true;
    ///     });
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddPragmaticEndpoints(
        this IServiceCollection services,
        Action<PragmaticEndpointsOptions>? configure = null)
    {
        var options = new PragmaticEndpointsOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        return services;
    }
}