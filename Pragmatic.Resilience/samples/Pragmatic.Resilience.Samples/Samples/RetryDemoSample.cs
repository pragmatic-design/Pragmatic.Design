using Pragmatic.Resilience.Pipeline;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Samples.Samples;

/// <summary>
///     Runnable retry demo: simulates transient failures, shows retry with backoff.
/// </summary>
public static class RetryDemoSample
{
    private static int _callCount;

    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("5. Retry Demo — Transient Failure Recovery");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        await ShowRetrySuccess();
        await ShowRetryExhausted();

        Console.WriteLine();
    }

    private static async Task ShowRetrySuccess()
    {
        Console.WriteLine("  5.1 Retry succeeds on 3rd attempt");
        Console.WriteLine("  ------------------------------------");

        _callCount = 0;
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(o =>
            {
                o.MaxRetries = 3;
                o.BaseDelay = TimeSpan.FromMilliseconds(10);
                o.BackoffType = BackoffType.Linear;
            })
            .Build();

        var ctx = new ResilienceContext { OperationName = "transient-api" };
        var result = await pipeline.ExecuteAsync<string>((c, ct) =>
        {
            _callCount++;
            Console.WriteLine($"    Attempt {_callCount}...");
            if (_callCount < 3)
                throw new HttpRequestException("Service unavailable (simulated)");
            return Task.FromResult("Success!");
        }, ctx);

        Console.WriteLine($"    Result: \"{result}\" after {_callCount} attempts");
        Console.WriteLine();
    }

    private static async Task ShowRetryExhausted()
    {
        Console.WriteLine("  5.2 Retry exhausted — all attempts fail");
        Console.WriteLine("  -------------------------------------------");

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(o =>
            {
                o.MaxRetries = 2;
                o.BaseDelay = TimeSpan.FromMilliseconds(10);
            })
            .Build();

        try
        {
            var ctx = new ResilienceContext { OperationName = "always-fail" };
            await pipeline.ExecuteAsync<string>((c, ct) =>
                Task.FromException<string>(new TimeoutException("Always fails")), ctx);
        }
        catch (RetryExhaustedException ex)
        {
            Console.WriteLine($"    RetryExhaustedException: \"{ex.Message}\"");
            Console.WriteLine($"    Inner: {ex.InnerException?.GetType().Name}");
        }

        Console.WriteLine();
    }
}
