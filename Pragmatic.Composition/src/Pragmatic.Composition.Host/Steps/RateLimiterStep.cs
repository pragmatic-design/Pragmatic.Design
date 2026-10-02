using Microsoft.AspNetCore.Builder;
using Pragmatic.Composition.Abstractions;

namespace Pragmatic.Composition.Steps;

/// <summary>
///     Adds the rate limiting middleware, without which every declared limit is inert.
/// </summary>
/// <remarks>
///     <para>
///         <c>[RateLimit]</c> generates the policy and puts <c>RequireRateLimiting</c> on the route,
///         and both are metadata: nothing enforces them until this middleware runs. Without it the
///         attribute registers a limit and permits every request.
///     </para>
///     <para>
///         Order 92 — after routing (50), so the endpoint and its policy are resolved, and after
///         authentication (91), so a limiter partitioned by the caller has a caller to read. Today's
///         limiters are unpartitioned and would not care; the ordering is what makes partitioning
///         possible without moving the step later.
///     </para>
///     <para>
///         Pass-through where nothing declares a limit: the middleware only acts on endpoints
///         carrying the metadata.
///     </para>
/// </remarks>
public class RateLimiterStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 92;

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
        => app.UseRateLimiter();
}
