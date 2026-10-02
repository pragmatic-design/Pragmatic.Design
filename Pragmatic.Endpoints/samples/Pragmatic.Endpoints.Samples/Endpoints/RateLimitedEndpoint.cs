using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Base;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Samples.Endpoints;

/// <summary>
///     Demonstrates inline rate limiting using ASP.NET Core's built-in fixed window limiter.
///     The SG generates:
///     1. AddFixedWindowLimiter("__pragmatic_ratelimit_RateLimitedEndpoint", ...) in AddPragmaticEndpoints()
///     2. .RequireRateLimiting("__pragmatic_ratelimit_RateLimitedEndpoint") on the endpoint
/// </summary>
/// <remarks>
///     Test with: <c>for i in {1..10}; do curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5200/api/limited; done</c>
///     After 5 requests in 1 minute, you'll get 429 Too Many Requests.
/// </remarks>
[Endpoint(HttpVerb.Get, "/limited")]
[ApiSummary("Rate Limited")]
[ApiTags("General")]
[RateLimit(Requests = 5, Window = "1m")]
public partial class RateLimitedEndpoint : Endpoint<RateLimitResponse>
{
    public override Task<Result<RateLimitResponse>> HandleAsync(CancellationToken ct = default)
    {
        return Task.FromResult(Result<RateLimitResponse>.Success(
            new RateLimitResponse(DateTimeOffset.UtcNow, "Request accepted")));
    }
}

public record RateLimitResponse(DateTimeOffset Timestamp, string Message);
