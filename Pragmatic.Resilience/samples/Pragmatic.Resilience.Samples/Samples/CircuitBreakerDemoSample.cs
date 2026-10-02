using Pragmatic.Resilience.Pipeline;
using Pragmatic.Resilience.State;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Samples.Samples;

/// <summary>
///     Circuit breaker: fail-fast after threshold, break duration, half-open probe.
/// </summary>
public static class CircuitBreakerDemoSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("7. Circuit Breaker — Fail-Fast After Threshold");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var stateStore = new InMemoryCircuitBreakerStateStore();

        var pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(stateStore, o =>
            {
                o.FailureThreshold = 3;
                o.BreakDuration = TimeSpan.FromMilliseconds(500);
            })
            .Build();

        Console.WriteLine("  State transitions: Closed → Open → HalfOpen → Closed");
        Console.WriteLine("  FailureThreshold=3, BreakDuration=500ms");
        Console.WriteLine();

        // Cause 3 failures to trip the circuit
        for (var i = 1; i <= 4; i++)
        {
            try
            {
                var ctx = new ResilienceContext { OperationName = $"call-{i}" };
                await pipeline.ExecuteAsync<string>((c, ct) =>
                    Task.FromException<string>(new HttpRequestException("Service down")), ctx);
            }
            catch (CircuitBrokenException)
            {
                Console.WriteLine($"    Call {i}: CircuitBrokenException (circuit is OPEN — fail-fast)");
            }
            catch (HttpRequestException)
            {
                Console.WriteLine($"    Call {i}: HttpRequestException (circuit still tracking failures)");
            }
        }

        Console.WriteLine();
        Console.WriteLine("  After 3 failures, circuit opens → call 4 fails immediately.");
        Console.WriteLine("  After BreakDuration (500ms), circuit moves to HalfOpen.");
        Console.WriteLine("  Next successful call → circuit closes (recovery).");
        Console.WriteLine();
    }
}
