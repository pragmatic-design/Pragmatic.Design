using Pragmatic.Resilience.Pipeline;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Samples.Samples;

/// <summary>
///     Demonstrates the fallback strategy: when the primary operation throws a
///     handled exception, a substitute value is returned instead of failing.
///     The <see cref="FallbackOptions{TResult}.ShouldHandle" /> predicate scopes
///     which exceptions are eligible for the fallback.
/// </summary>
public static class FallbackDemoSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("10. Fallback — Alternative Value on Failure");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var pipeline = new ResiliencePipelineBuilder()
            .AddFallback(new FallbackOptions<string>
            {
                // Only fall back for the simulated transient failure.
                ShouldHandle = ex => ex is InvalidOperationException,
                FallbackAction = (ex, _, _) =>
                {
                    Console.WriteLine($"    Primary failed ({ex.Message}); serving cached value");
                    return Task.FromResult("cached: last known good value");
                },
            })
            .Build();

        // 1. Primary fails with a handled exception → fallback value is returned.
        var fromFallback = await pipeline.ExecuteAsync<string>(
            (c, ct) => throw new InvalidOperationException("primary data source unavailable"),
            new ResilienceContext { OperationName = "load-config" });
        Console.WriteLine($"    Result: \"{fromFallback}\"");
        Console.WriteLine();

        // 2. Primary succeeds → fallback is not used.
        var fromPrimary = await pipeline.ExecuteAsync<string>(async (c, ct) =>
        {
            await Task.Delay(10, ct).ConfigureAwait(false);
            return "fresh: live value from primary";
        }, new ResilienceContext { OperationName = "load-config" });
        Console.WriteLine($"    Result: \"{fromPrimary}\"");
        Console.WriteLine();
    }
}
