using Pragmatic.Resilience.Pipeline;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Samples.Samples;

/// <summary>
///     Demonstrates the sliding-window rate limiter: up to <c>MaxRequests</c> calls
///     are allowed within the rolling <c>Window</c>; further calls are rejected
///     with <see cref="RateLimitRejectedException" /> until older timestamps fall
///     out of the window. A controllable <see cref="TimeProvider" /> makes the demo
///     deterministic (no wall-clock waits).
/// </summary>
public static class RateLimiterDemoSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("9. Rate Limiter — Sliding Window");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // Fake time so the window advances deterministically without real delays.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var pipeline = new ResiliencePipelineBuilder()
            .AddStrategy(new RateLimiterStrategy(
                new RateLimiterOptions
                {
                    MaxRequests = 3,
                    Window = TimeSpan.FromSeconds(1),
                },
                clock))
            .Build();

        Console.WriteLine("    MaxRequests=3 per 1s window");
        Console.WriteLine();

        // First 3 calls fill the window; the next 2 are rejected.
        for (var i = 1; i <= 5; i++)
            await TryCall(pipeline, i);

        // Advance the clock past the window so timestamps expire, then retry.
        Console.WriteLine("    Advancing clock by 1.1s (window resets)...");
        clock.Advance(TimeSpan.FromMilliseconds(1100));
        await TryCall(pipeline, 6);

        Console.WriteLine();
    }

    private static async Task TryCall(IResiliencePipeline pipeline, int i)
    {
        var ctx = new ResilienceContext { OperationName = $"call-{i}" };
        try
        {
            var result = await pipeline.ExecuteAsync<string>(
                (c, ct) => Task.FromResult($"call {i} executed"), ctx);
            Console.WriteLine($"    {result}");
        }
        catch (RateLimitRejectedException ex)
        {
            Console.WriteLine($"    Call {i} rejected — {ex.Message}");
        }
    }

    /// <summary>
    ///     Minimal manually-advanced <see cref="TimeProvider" /> for deterministic samples.
    /// </summary>
    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
