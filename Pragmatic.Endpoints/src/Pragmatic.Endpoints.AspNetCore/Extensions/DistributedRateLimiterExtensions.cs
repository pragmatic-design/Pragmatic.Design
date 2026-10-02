using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching;

namespace Pragmatic.Endpoints.AspNetCore.Extensions;

/// <summary>
///     Extension methods for bridging ASP.NET Core Rate Limiting with Pragmatic.Caching.
/// </summary>
public static class DistributedRateLimiterExtensions
{
    /// <summary>
    ///     Replaces in-memory rate limiters with distributed rate limiters
    ///     backed by Pragmatic.Caching's <c>ICacheStack</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         When this is enabled, rate limit counters are stored in the same distributed
    ///         cache backend (Redis, SQL, etc.) as other Pragmatic.Caching entries.
    ///         This allows rate limiting to work correctly across multiple app instances.
    ///     </para>
    ///     <para>
    ///         Requires <c>Pragmatic.Caching</c> to be registered in DI.
    ///         Uses fixed-window counters with cache TTL for window expiry.
    ///     </para>
    ///     <para>
    ///         When the cache is unavailable, the limiter fails-closed (rejects all requests)
    ///         to prevent DoS during a cache outage.
    ///     </para>
    ///     <para>
    ///         Partition key resolution order: authenticated user name → connection remote IP →
    ///         "anonymous". Raw <c>X-Forwarded-For</c>/<c>X-Real-IP</c> headers are NOT trusted —
    ///         a direct client could rotate them to mint fresh partitions and bypass the limit. If
    ///         your app runs behind a reverse proxy, configure ASP.NET Core's forwarded-headers
    ///         middleware (<c>UseForwardedHeaders</c> with <c>KnownProxies</c>/<c>KnownNetworks</c>)
    ///         so <c>Connection.RemoteIpAddress</c> reflects the real client through trusted proxies.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>
    /// builder.Services.AddPragmaticCaching();
    /// builder.Services.UseDistributedRateLimiterFromPragmaticCaching("standard", permitLimit: 100, window: TimeSpan.FromMinutes(1));
    /// app.UseRateLimiter();
    ///     </code>
    /// </example>
    public static IServiceCollection UseDistributedRateLimiterFromPragmaticCaching(
        this IServiceCollection services,
        string policyName,
        int permitLimit,
        TimeSpan window)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy(policyName, httpContext =>
            {
                var cache = CacheStackProvider.ForCategory<CacheCategories.RateLimiting>(httpContext.RequestServices);
                if (cache is null)
                {
                    // Fail-closed: cache unavailable → reject all requests to prevent DoS during outage.
                    // Use a zero-permit fixed-window limiter so the 429 rejection path is exercised normally.
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: "fail-closed",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 0,
                            Window = TimeSpan.FromSeconds(1),
                            QueueLimit = 0
                        });
                }

                var partitionKey = ResolveRateLimitPartitionKey(httpContext);

                return RateLimitPartition.Get(partitionKey, key =>
                    new PragmaticDistributedRateLimiter(cache, $"{policyName}:{key}", permitLimit, window));
            });
        });

        return services;
    }

    /// <summary>
    ///     Computes the rate-limit partition key for a request: the authenticated identity name,
    ///     else the connection remote IP, else <c>"anonymous"</c>.
    /// </summary>
    /// <remarks>
    ///     Raw <c>X-Forwarded-For</c>/<c>X-Real-IP</c> headers are deliberately NOT used —
    ///     a direct client could rotate them to mint fresh partitions and bypass the limit. Behind a
    ///     reverse proxy, configure the forwarded-headers middleware with trusted proxies so that
    ///     <see cref="ConnectionInfo.RemoteIpAddress" /> reflects the real client.
    /// </remarks>
    public static string ResolveRateLimitPartitionKey(HttpContext httpContext)
        => httpContext.User.Identity?.Name
           ?? httpContext.Connection.RemoteIpAddress?.ToString()
           ?? "anonymous";
}
