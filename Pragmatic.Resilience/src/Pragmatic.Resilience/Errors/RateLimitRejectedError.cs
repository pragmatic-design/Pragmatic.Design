using Pragmatic.Result;

namespace Pragmatic.Resilience.Errors;

/// <summary>Rate limit exceeded — too many requests in the time window.</summary>
public sealed record RateLimitRejectedError(int MaxRequests, TimeSpan Window) : Error
{
    public override string Code => "RATE_LIMIT_REJECTED";
    public override int StatusCode => 429;
    public override string Title { get; } = $"Rate limit exceeded: {MaxRequests} requests per {Window.TotalSeconds:F0}s window";
}
