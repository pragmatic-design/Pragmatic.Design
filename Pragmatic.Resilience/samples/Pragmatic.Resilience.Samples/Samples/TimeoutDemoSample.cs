using Pragmatic.Resilience.Pipeline;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Samples.Samples;

/// <summary>
///     Timeout demo and combined strategy patterns.
/// </summary>
public static class TimeoutDemoSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("6. Timeout & Combined Strategies");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        await ShowTimeoutSuccess();
        ShowCombinedPattern();

        Console.WriteLine();
    }

    private static async Task ShowTimeoutSuccess()
    {
        Console.WriteLine("  6.1 Timeout — fast operation completes within limit");
        Console.WriteLine("  -------------------------------------------------------");

        var pipeline = new ResiliencePipelineBuilder()
            .AddTimeout(o => o.Timeout = TimeSpan.FromSeconds(5))
            .Build();

        var ctx = new ResilienceContext { OperationName = "fast-op" };
        var result = await pipeline.ExecuteAsync<string>(async (c, ct) =>
        {
            await Task.Delay(10, ct);
            return "Completed within timeout";
        }, ctx);

        Console.WriteLine($"    Result: \"{result}\"");
        Console.WriteLine();
    }

    private static void ShowCombinedPattern()
    {
        Console.WriteLine("  6.2 Combined strategies — composition order matters");
        Console.WriteLine("  -------------------------------------------------------");

        Console.WriteLine("""
            // Strategies execute outermost → innermost:
            // Timeout(Retry(CircuitBreaker(operation)))

            var pipeline = new ResiliencePipelineBuilder()
                .AddTimeout(o => o.Timeout = TimeSpan.FromSeconds(30))  // Outermost: total budget
                .AddRetry(o =>                                          // Middle: retry transient
                {
                    o.MaxRetries = 3;
                    o.BaseDelay = TimeSpan.FromSeconds(1);
                    o.BackoffType = BackoffType.Exponential;
                })
                .AddCircuitBreaker(store, o =>                          // Innermost: fail-fast
                {
                    o.FailureThreshold = 5;
                    o.BreakDuration = TimeSpan.FromSeconds(30);
                })
                .Build();

            // Flow:
            // 1. Timeout starts 30s timer
            // 2. Retry attempts operation up to 3 times
            // 3. CircuitBreaker tracks failures, breaks after 5
            // 4. If circuit is open → RetryExhaustedError (no attempt made)
            // 5. If total time > 30s → TimeoutException cancels everything
        """);
        Console.WriteLine();
    }
}
