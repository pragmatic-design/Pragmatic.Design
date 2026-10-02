using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Authorization;

namespace Pragmatic.Identity.Authorization;

/// <summary>
///     DI registration for Pragmatic authorization policy bridge.
///     Registers <see cref="PragmaticPermissionHandler" /> so that ASP.NET Core's
///     authorization middleware delegates permission checks to <see cref="IPermissionChecker" />.
/// </summary>
public static class AuthorizationServiceExtensions
{
    /// <summary>
    ///     Adds the Pragmatic permission authorization handler.
    ///     Call after <c>AddPragmaticIdentity()</c> and <c>AddAuthorization()</c>.
    /// </summary>
    public static IServiceCollection AddPragmaticAuthorization(this IServiceCollection services)
    {
        services.TryAddScoped<IAuthorizationHandler, PragmaticPermissionHandler>();
        ReplaceDefaultResultHandler(services);

        return services;
    }

    /// <summary>
    ///     Installs <see cref="PragmaticAuthorizationResultHandler" /> unless the application already
    ///     brought its own.
    /// </summary>
    /// <remarks>
    ///     Not <c>TryAdd</c>: every entry point calls <c>AddAuthorization()</c> first, and that
    ///     registers ASP.NET's <see cref="AuthorizationMiddlewareResultHandler" /> the same way — so a
    ///     <c>TryAdd</c> here is silently a no-op and every 403 keeps coming back with an empty body,
    ///     which is exactly what a consumer reported on a build that already contained this class.
    ///     Only that one default is displaced; a handler the application registered itself is left
    ///     alone, because replacing it would be taking a decision that is not ours.
    /// </remarks>
    private static void ReplaceDefaultResultHandler(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType != typeof(IAuthorizationMiddlewareResultHandler))
                continue;

            if (descriptor.ImplementationType != typeof(AuthorizationMiddlewareResultHandler))
                return;

            services.RemoveAt(i);
        }

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, PragmaticAuthorizationResultHandler>();
    }
}
