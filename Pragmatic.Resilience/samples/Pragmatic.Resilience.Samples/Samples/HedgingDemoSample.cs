using Pragmatic.Resilience.Pipeline;

namespace Pragmatic.Resilience.Samples.Samples;

/// <summary>
///     Demonstrates the hedging strategy: a slow first attempt is overtaken by a
///     faster hedged attempt launched after a short delay. The first attempt to
///     succeed wins; the slow one is cancelled. This is how hedging trims tail
///     latency for latency-sensitive operations.
/// </summary>
public static class HedgingDemoSample
{
    private static int _attempt;

    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("8. Hedging — Parallel Attempts, First-Wins");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        _attempt = 0;

        var pipeline = new ResiliencePipelineBuilder()
            .AddHedging(o =>
            {
                o.MaxAttempts = 3;
                // Short delay so the fast hedge can overtake the slow first attempt.
                o.Delay = TimeSpan.FromMilliseconds(30);
            })
            .Build();

        var ctx = new ResilienceContext { OperationName = "hedged-fetch" };

        var result = await pipeline.ExecuteAsync<string>(async (c, ct) =>
        {
            var current = Interlocked.Increment(ref _attempt);

            // First attempt is slow; subsequent hedged attempts are fast.
            var delayMs = current == 1 ? 500 : 10;
            Console.WriteLine($"    Attempt #{current} started (simulated latency {delayMs}ms)");
            await Task.Delay(delayMs, ct).ConfigureAwait(false);

            Console.WriteLine($"    Attempt #{current} produced a result");
            return $"response from attempt #{current}";
        }, ctx);

        Console.WriteLine($"    Winning result: \"{result}\"");
        Console.WriteLine();
    }
}
