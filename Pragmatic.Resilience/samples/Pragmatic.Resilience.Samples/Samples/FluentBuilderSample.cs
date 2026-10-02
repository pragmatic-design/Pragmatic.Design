using Pragmatic.Resilience.Pipeline;
using Pragmatic.Resilience.State;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Samples.Samples;

/// <summary>
///     Demonstrates standalone resilience pipelines using the fluent builder API.
///     No DI container needed — useful for console apps, scripts, and testing.
/// </summary>
public static class FluentBuilderSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ 1. Fluent Builder ═══");
        Console.WriteLine();

        // -------------------------------------------------------------------
        // 1a. Build a pipeline with timeout + retry + circuit breaker
        // -------------------------------------------------------------------

        var stateStore = new InMemoryCircuitBreakerStateStore();

        var pipeline = new ResiliencePipelineBuilder()
            .AddTimeout(o => o.Timeout = TimeSpan.FromSeconds(5))
            .AddRetry(o =>
            {
                o.MaxRetries = 3;
                o.BaseDelay = TimeSpan.FromMilliseconds(100);
                o.BackoffType = BackoffType.Exponential;
            })
            .AddCircuitBreaker(stateStore, o =>
            {
                o.FailureThreshold = 5;
                o.BreakDuration = TimeSpan.FromSeconds(30);
            })
            .Build();

        var result = await pipeline.ExecuteAsync(
            static (ctx, ct) =>
            {
                Console.WriteLine($"  Executing: {ctx.OperationName}");
                return Task.FromResult("OK");
            },
            new ResilienceContext { OperationName = "sample-operation" });

        Console.WriteLine($"  Result: {result}");
        Console.WriteLine();

        // -------------------------------------------------------------------
        // 1b. Timeout-only pipeline (simplest case)
        // -------------------------------------------------------------------

        var timeoutPipeline = new ResiliencePipelineBuilder()
            .AddTimeout(o => o.Timeout = TimeSpan.FromSeconds(2))
            .Build();

        var fast = await timeoutPipeline.ExecuteAsync(
            static (ctx, ct) => Task.FromResult("fast"),
            new ResilienceContext { OperationName = "fast-call" });

        Console.WriteLine($"  Timeout-only result: {fast}");
        Console.WriteLine();

        // -------------------------------------------------------------------
        // 1c. Bulkhead — limit concurrency
        // -------------------------------------------------------------------

        var bulkheadPipeline = new ResiliencePipelineBuilder()
            .AddBulkhead(o => o.MaxConcurrency = 2)
            .Build();

        var tasks = Enumerable.Range(1, 3).Select(async i =>
        {
            try
            {
                return await bulkheadPipeline.ExecuteAsync(
                    static (ctx, ct) =>
                    {
                        Console.WriteLine($"  Bulkhead slot acquired: {ctx.OperationName}");
                        return Task.FromResult($"done-{ctx.OperationName}");
                    },
                    new ResilienceContext { OperationName = $"task-{i}" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Bulkhead rejected task-{i}: {ex.Message}");
                return $"rejected-{i}";
            }
        });

        await Task.WhenAll(tasks);
        Console.WriteLine();
    }
}
