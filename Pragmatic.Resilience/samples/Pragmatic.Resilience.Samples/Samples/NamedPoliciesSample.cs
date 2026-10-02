using Pragmatic.Resilience.Configuration;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Samples.Samples;

/// <summary>
///     Demonstrates DI-based named resilience policies configured via options.
///     In production, these bind from <c>appsettings.json</c> section <c>"Resilience"</c>.
/// </summary>
public static class NamedPoliciesSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ 2. DI + Named Policies ═══");
        Console.WriteLine();

        var services = new ServiceCollection();

        // -------------------------------------------------------------------
        // 2a. Register multiple policies via AddPragmaticResilience
        // -------------------------------------------------------------------

        services.AddPragmaticResilience(options =>
        {
            // External API: aggressive retry + circuit breaker
            options.Policies["external-api"] = new ResiliencePolicyOptions
            {
                Timeout = new() { Timeout = TimeSpan.FromSeconds(10) },
                Retry = new()
                {
                    MaxRetries = 3,
                    BaseDelay = TimeSpan.FromMilliseconds(200),
                    BackoffType = BackoffType.Exponential
                },
                CircuitBreaker = new()
                {
                    FailureThreshold = 5,
                    BreakDuration = TimeSpan.FromSeconds(30)
                }
            };

            // Database: fast timeout, minimal retry
            options.Policies["database"] = new ResiliencePolicyOptions
            {
                Timeout = new() { Timeout = TimeSpan.FromSeconds(3) },
                Retry = new()
                {
                    MaxRetries = 2,
                    BaseDelay = TimeSpan.FromMilliseconds(50),
                    BackoffType = BackoffType.Constant
                }
            };
        });

        // -------------------------------------------------------------------
        // 2b. Add individual policies with AddResiliencePolicy
        // -------------------------------------------------------------------

        services.AddResiliencePolicy("cache", o =>
        {
            o.Timeout = new() { Timeout = TimeSpan.FromMilliseconds(500) };
            o.Bulkhead = new() { MaxConcurrency = 10 };
        });

        // -------------------------------------------------------------------
        // 2c. Resolve and execute through named pipelines
        // -------------------------------------------------------------------

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IResiliencePipelineProvider>();

        var apiPipeline = provider.GetPipeline("external-api");
        var dbPipeline = provider.GetPipeline("database");

        var apiResult = await apiPipeline.ExecuteAsync(
            static (ctx, ct) => Task.FromResult("API response"),
            new ResilienceContext { OperationName = "FetchUserProfile" });
        Console.WriteLine($"  external-api result: {apiResult}");

        var dbResult = await dbPipeline.ExecuteAsync(
            static (ctx, ct) => Task.FromResult(42),
            new ResilienceContext { OperationName = "GetOrderCount" });
        Console.WriteLine($"  database result: {dbResult}");

        // -------------------------------------------------------------------
        // 2d. Unknown policy → PassthroughPipeline (zero overhead)
        // -------------------------------------------------------------------

        var unknownPipeline = provider.GetPipeline("nonexistent");
        Console.WriteLine($"  Unknown policy type: {unknownPipeline.GetType().Name}");
        Console.WriteLine();
    }
}
