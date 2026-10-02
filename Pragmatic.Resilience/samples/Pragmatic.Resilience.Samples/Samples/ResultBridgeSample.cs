using Pragmatic.Resilience.Bridge;
using Pragmatic.Resilience.Pipeline;
using Pragmatic.Result;

namespace Pragmatic.Resilience.Samples.Samples;

/// <summary>
///     Demonstrates <see cref="ResilienceResultBridge.ExecuteAsResultAsync{T}" />,
///     which runs an operation through a pipeline and converts strategy exceptions
///     into typed <see cref="IError" /> values instead of throwing — keeping call
///     sites on the Result-over-exceptions path.
/// </summary>
public static class ResultBridgeSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("11. Result Bridge — Exception → Typed Error");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(o =>
            {
                o.MaxRetries = 2;
                o.BaseDelay = TimeSpan.FromMilliseconds(10);
            })
            .Build();

        // 1. Success path → Result.Success carrying the value.
        Result<string, IError> success = await pipeline.ExecuteAsResultAsync(async ct =>
        {
            await Task.Delay(10, ct).ConfigureAwait(false);
            return "payload";
        });
        Console.WriteLine(success.IsSuccess
            ? $"    Success → value: \"{success.Value}\""
            : $"    Failure → {success.Error.Title}");

        // 2. Failure path → retry exhausts, the exception is mapped to a typed
        //    RetryExhaustedError (no exception escapes the bridge).
        Result<string, IError> failure = await pipeline.ExecuteAsResultAsync<string>(
            ct => throw new InvalidOperationException("downstream keeps failing"));
        Console.WriteLine(failure.IsSuccess
            ? $"    Success → value: \"{failure.Value}\""
            : $"    Failure → {failure.Error.GetType().Name}: {failure.Error.Title}");

        // 3. Void overload → VoidResult<IError> for fire-and-forget operations.
        VoidResult<IError> voidResult = await pipeline.ExecuteAsResultAsync(async ct =>
        {
            await Task.Delay(10, ct).ConfigureAwait(false);
        });
        Console.WriteLine($"    Void operation succeeded: {voidResult.IsSuccess}");
        Console.WriteLine();
    }
}
