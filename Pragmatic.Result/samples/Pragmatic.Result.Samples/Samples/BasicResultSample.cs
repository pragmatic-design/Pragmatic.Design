// =============================================================================
// Basic Result<TValue, TError> usage
// =============================================================================

using Pragmatic.Result.Samples.Errors;

namespace Pragmatic.Result.Samples.Samples;

public static class BasicResultSample
{
    public static void Run()
    {
        Console.WriteLine("1. Basic Result<TValue, TError> usage");
        Console.WriteLine("--------------------------------------");

        var success = Result<int, ValidationError>.Success(42);
        var failure = Result<int, ValidationError>.Failure(new ValidationError("Invalid input"));

        // Pattern matching with Match
        Console.WriteLine($"Success value: {success.Match(v => v.ToString(), e => "error")}");
        Console.WriteLine($"Failure value: {failure.Match(v => v.ToString(), e => e.Message)}");

        // TryGet pattern
        if (success.TryGetValue(out var value))
            Console.WriteLine($"Got value: {value}");

        // Implicit conversion
        Result<int, ValidationError> implicitSuccess = 100;
        Result<int, ValidationError> implicitFailure = new ValidationError("Bad");
        Console.WriteLine($"Implicit success: {implicitSuccess.IsSuccess}");
        Console.WriteLine($"Implicit failure: {implicitFailure.IsFailure}");
        Console.WriteLine();
    }
}