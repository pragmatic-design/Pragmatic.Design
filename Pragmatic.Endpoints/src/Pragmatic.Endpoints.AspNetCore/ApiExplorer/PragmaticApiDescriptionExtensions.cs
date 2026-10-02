using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pragmatic.Endpoints.ApiExplorer;

/// <summary>
///     Registers <see cref="PragmaticApiDescriptionProvider" />.
/// </summary>
public static class PragmaticApiDescriptionExtensions
{
    /// <summary>
    ///     Lets the API explorer, and so the runtime OpenAPI document, see the generated endpoints.
    /// </summary>
    /// <remarks>
    ///     Called by the generated endpoint registration, so a host does not call it. It costs nothing
    ///     where no document is built: the explorer is the only thing that resolves a description
    ///     provider.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPragmaticApiDescriptions(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Transient<IApiDescriptionProvider, PragmaticApiDescriptionProvider>());
        return services;
    }
}
