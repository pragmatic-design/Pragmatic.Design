using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Authorization;
using Pragmatic.Endpoints.Authorization;
using Pragmatic.Identity.Authorization;

namespace Pragmatic.Identity;

/// <summary>
///     DI registration extensions for Pragmatic.Identity.
/// </summary>
public static class IdentityServiceExtensions
{
    /// <summary>
    ///     Registers <see cref="ICurrentUser" /> backed by the HTTP context's ClaimsPrincipal.
    ///     Also registers <see cref="ClaimsPermissionChecker" /> as the default <see cref="IPermissionChecker" />
    ///     and <see cref="PragmaticPermissionHandler" /> for ASP.NET Core authorization policy integration.
    ///     Optionally configure claim type mapping via <see cref="IdentityOptions" />.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration for claim type mapping.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPragmaticIdentity(
        this IServiceCollection services,
        Action<IdentityOptions>? configure = null)
    {
        services.AddHttpContextAccessor();

        // Through the options pattern, whether or not a configure is given. The generated host calls
        // this with none, and registering Options.Create(new IdentityOptions()) as a fixed
        // IOptions<IdentityOptions> would be wrong: a closed registration wins over OptionsManager, so
        // every Configure<IdentityOptions> — an authentication entry point's, the application's own —
        // would be ignored without a word, and a bearer token's name would never reach DisplayName.
        var options = services.AddOptions<IdentityOptions>();
        if (configure is not null)
            options.Configure(configure);

        services.TryAddScoped<ICurrentUser, ClaimsPrincipalUserAccessor>();
        services.TryAddScoped<IPermissionChecker, ClaimsPermissionChecker>();

        // ASP.NET Core authorization handler for [RequirePermission] → PragmaticPermissionRequirement
        services.TryAddScoped<IAuthorizationHandler, PragmaticPermissionHandler>();

        return services;
    }
}
