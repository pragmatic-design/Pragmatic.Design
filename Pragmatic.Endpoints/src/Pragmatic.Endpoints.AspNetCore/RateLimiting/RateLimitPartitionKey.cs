using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Endpoints.RateLimiting;

/// <summary>
///     Who a rate limit counts against.
/// </summary>
/// <remarks>
///     <para>
///         A limit declared as "3 requests per second" reads as a quota per caller, because that is
///         what the phrase means everywhere else. It was one bucket for the whole route: every caller
///         drew from the same permits, so in a multi-tenant deployment one tenant in a retry loop
///         refused every other tenant, at no cost and with no privilege. Measured with two identities
///         against the same endpoint — the second found the budget already spent.
///     </para>
///     <para>
///         The key is the narrowest thing that identifies the caller: the tenant if one is resolved,
///         otherwise the authenticated user, otherwise the remote address. A request that is none of
///         those — no tenant, anonymous, no address, which in practice means an in-process test host —
///         falls back to a shared bucket, because refusing to limit would be worse than limiting
///         coarsely.
///     </para>
///     <para>
///         A deliberately global cap is still available, and is now the thing you have to ask for: a
///         named policy configured through <c>PragmaticEndpointsOptions.ConfigureRateLimiter</c>.
///     </para>
/// </remarks>
public static class RateLimitPartitionKey
{
    /// <summary>The partition a request belongs to, prefixed so two sources cannot collide.</summary>
    /// <param name="context">The request. Its services are resolved per call, not captured.</param>
    /// <param name="policyName">Keeps one endpoint's permits from being spent by another.</param>
    public static string For(HttpContext context, string policyName)
    {
        // Resolved from the request, because the limiter is registered once and serves every request.
        var tenant = context.RequestServices.GetService<ITenantContext>()?.TenantId;
        if (!string.IsNullOrEmpty(tenant))
            return $"{policyName}|t:{tenant}";

        var user = context.RequestServices.GetService<ICurrentUser>();
        if (user is { IsAuthenticated: true, Id.Length: > 0 })
            return $"{policyName}|u:{user.Id}";

        var address = context.Connection.RemoteIpAddress;
        return address is not null
            ? $"{policyName}|a:{address}"
            : $"{policyName}|shared";
    }
}
