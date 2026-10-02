using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Composition.Abstractions;
using Pragmatic.Composition.Steps;
using Pragmatic.Http;
using Pragmatic.Identity.Local.Actions;

namespace Pragmatic.Identity.Local.Jwt;

/// <summary>
///     IP-partitioned rate limiting of the local sign-in endpoints (audit item A2): the per-account lockout
///     in <c>LoginUser</c> stops brute force against one account; this caps credential spraying from one
///     address, which the lockout cannot see.
/// </summary>
/// <remarks>
///     <para>
///         Registered by <c>UseJwtAuthentication</c>; without that registration the sign-in is limited
///         by the lockout alone. Its enablement does not live in a step's instance field: the host calls
///         <c>ConfigureServices</c> and <c>ConfigurePipeline</c> on different instances, so such a step,
///         even registered, would never add its middleware.
///     </para>
///     <para>
///         The endpoints are recognised by the operation they run (<see cref="ExposedOperationMetadata" />),
///         not by a path: the route is the application's choice, and a fixed prefix such as
///         <c>/identity/local/login</c> is not the one every application exposes.
///     </para>
///     <para>
///         ⚠️ It composes with the application's limits instead of replacing them: the global limiter is
///         chained with any other, the rejection status is set only for sign-in rejections, and the
///         middleware is <see cref="RateLimiterStep" /> — the one the host already adds for endpoint
///         limits — so it runs once.
///     </para>
/// </remarks>
internal static class LoginRateLimit
{
    private const string Section = "Identity:Local:RateLimit";

    /// <summary>The operations that check a password: the ones a sprayer calls.</summary>
    private static readonly Type[] SignInOperations = [typeof(LoginUser), typeof(SignInUser)];

    /// <summary>Adds the limiter and the middleware that enforces it, unless the configuration turns it off.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A limit that would reject every sign-in or none.</exception>
    public static void Register(IServiceCollection services, IConfiguration configuration)
    {
        var options = Read(configuration.GetSection(Section));
        if (!options.Enabled)
            return;

        // Fail fast on misconfigured limits — a non-positive PermitLimit or Window would either reject
        // every sign-in or throw deep inside the rate limiter at request time.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.PermitLimit, $"{Section}:PermitLimit");
        if (options.Window <= TimeSpan.Zero || options.Window > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(
                $"{Section}:Window", options.Window, "Window must be greater than zero and at most one day.");

        services.AddRateLimiter(rateLimiter =>
        {
            var signIn = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                if (!IsSignIn(httpContext))
                    return RateLimitPartition.GetNoLimiter("__unlimited");

                // The connection's remote IP. We deliberately do NOT read X-Forwarded-For directly: a client
                // can spoof that header to land in a fresh bucket and defeat the spray cap. Operators behind
                // a trusted load balancer configure UseForwardedHeaders (KnownProxies/KnownNetworks) so
                // RemoteIpAddress reflects the real client — that validates the proxy chain; a raw header
                // read does not.
                var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"sign-in:{clientIp}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        Window = options.Window,
                        QueueLimit = 0,
                    });
            });

            rateLimiter.GlobalLimiter = rateLimiter.GlobalLimiter is { } other
                ? PartitionedRateLimiter.CreateChained(other, signIn)
                : signIn;

            var onRejected = rateLimiter.OnRejected;
            rateLimiter.OnRejected = async (context, ct) =>
            {
                if (IsSignIn(context.HttpContext))
                    context.HttpContext.Response.StatusCode = options.RejectionStatusCode;
                if (onRejected is not null)
                    await onRejected(context, ct).ConfigureAwait(false);
            };
        });

        // The host adds this step where some module exposes an endpoint, before the configure callback
        // this runs in; a host whose only endpoints are exposed ones gets it here. By implementation type:
        // two instances would run the middleware twice, and every sign-in would spend two permits.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupStep, RateLimiterStep>());
    }

    private static bool IsSignIn(HttpContext httpContext)
        => httpContext.GetEndpoint()?.Metadata.GetMetadata<ExposedOperationMetadata>() is { } exposed
           && Array.IndexOf(SignInOperations, exposed.OperationType) >= 0;

    /// <summary>Key by key, as the rest of the section's readers do; a value of the wrong type names its key.</summary>
    private static LoginRateLimitOptions Read(IConfigurationSection section)
    {
        var options = new LoginRateLimitOptions();

        if (section["Enabled"] is { } enabled)
            options.Enabled = bool.TryParse(enabled, out var parsed)
                ? parsed
                : throw Malformed(section, "Enabled", enabled);
        if (section["PermitLimit"] is { } permitLimit)
            options.PermitLimit = int.TryParse(permitLimit, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw Malformed(section, "PermitLimit", permitLimit);
        if (section["Window"] is { } window)
            options.Window = TimeSpan.TryParse(window, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw Malformed(section, "Window", window);
        if (section["RejectionStatusCode"] is { } status)
            options.RejectionStatusCode = int.TryParse(status, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw Malformed(section, "RejectionStatusCode", status);

        return options;
    }

    private static InvalidOperationException Malformed(IConfigurationSection section, string key, string value)
        => new($"{section.Path}:{key} is '{value}', which is not a valid value for it.");
}
