using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Refuses to start a host whose endpoints require authorization while nothing can authenticate.
/// </summary>
/// <remarks>
///     <para>
///         The endpoints require authorization by default, and the authorization middleware is added only
///         when an authentication stack is registered (<c>AuthenticationStep</c>, <c>AuthorizationStep</c>).
///         A host that configures its authentication for one environment — <c>UseDevelopmentIdentity()</c>
///         in Development, the real scheme left for later — started in another one normally, and answered
///         500 to every protected request: "contains authorization metadata, but a middleware was not found
///         that supports authorization". A scaffold started without a launch profile runs in Production and
///         did exactly that.
///     </para>
///     <para>
///         Checked after the endpoints are mapped, because what they require is known only then: the
///         generated root group requires authorization unless the host is <c>[AnonymousHost]</c> or turns
///         <c>RequireAuthorizationByDefault</c> off, and an endpoint can require it on its own.
///     </para>
/// </remarks>
public static class AuthenticationRequirement
{
    /// <summary>Throws when an endpoint requires authorization and no authentication method is configured.</summary>
    /// <param name="endpoints">The application, its endpoints mapped.</param>
    /// <param name="environment">The environment the host runs in, named in the refusal.</param>
    public static async Task VerifyAsync(IEndpointRouteBuilder endpoints, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(environment);

        var protectedEndpoints = endpoints.DataSources
            .SelectMany(source => source.Endpoints)
            .Where(RequiresAuthorization)
            .ToArray();

        if (protectedEndpoints.Length == 0 || await CanAuthenticateAsync(endpoints.ServiceProvider).ConfigureAwait(false))
            return;

        throw new InvalidOperationException(
            $"The endpoints require authorization and no authentication method is configured for environment " +
            $"{environment.EnvironmentName}: {protectedEndpoints.Length} endpoint(s) would answer 500, " +
            $"'{protectedEndpoints[0].DisplayName}' among them. Configure one for this environment — " +
            "UseJwtAuthentication(), UseKeycloakAuthentication(), UseOidcAuthentication(), UseAuthentication<THandler>(), " +
            "or UseDevelopmentIdentity() in Development — or declare the host [AnonymousHost] if it has no users.");
    }

    /// <summary>What ASP.NET's endpoint middleware refuses to run without the authorization middleware.</summary>
    private static bool RequiresAuthorization(Endpoint endpoint)
        => endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null
           || endpoint.Metadata.GetMetadata<AuthorizationPolicy>() is not null;

    /// <summary>
    ///     An authentication stack with a scheme to challenge with, and the authorization services the
    ///     middleware needs — the conditions <c>AuthorizationStep</c> adds the middleware under.
    /// </summary>
    private static async Task<bool> CanAuthenticateAsync(IServiceProvider services)
    {
        if (services.GetService<IAuthorizationPolicyProvider>() is null)
            return false;

        return services.GetService<IAuthenticationSchemeProvider>() is { } schemes
               && await schemes.GetDefaultChallengeSchemeAsync().ConfigureAwait(false) is not null;
    }
}
