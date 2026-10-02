using Pragmatic.Result.Extensions;
using Pragmatic.Result.Samples.Errors;

namespace Pragmatic.Result.Samples.Samples;

/// <summary>
///     Recovery and error bridging: Ensure pipeline, implicit conversions,
///     nullable-to-result bridging patterns.
/// </summary>
public static class RecoverySample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("10. Recovery & Bridging Patterns");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowNullableBridge();
        ShowEnsureChain();
        ShowImplicitConversion();

        Console.WriteLine();
    }

    private static void ShowNullableBridge()
    {
        Console.WriteLine("  10.1 Nullable → Result bridge");
        Console.WriteLine("  --------------------------------");

        string? found = "Alice";
        string? missing = null;

        // Use Match on Maybe-like pattern
        var result1 = found is not null
            ? Result<string, IError>.Success(found)
            : Result<string, IError>.Failure(new ValidationError("User not found"));

        var result2 = missing is not null
            ? Result<string, IError>.Success(missing)
            : Result<string, IError>.Failure(new ValidationError("User not found"));

        Console.WriteLine($"    \"Alice\" → IsSuccess={result1.IsSuccess}, Value=\"{result1.Value}\"");
        Console.WriteLine($"    null   → IsFailure={result2.IsFailure}, Error.Code=\"{result2.Error.Code}\"");
        Console.WriteLine();
    }

    private static void ShowEnsureChain()
    {
        Console.WriteLine("  10.2 Ensure chain — multiple validations in pipeline");
        Console.WriteLine("  -------------------------------------------------------");

        var result = Result<string, IError>.Success("hello@example.com")
            .Ensure(
                email => email.Contains('@'),
                _ => new ValidationError("Must contain @"))
            .Ensure(
                email => email.Length >= 5,
                _ => new ValidationError("Must be at least 5 chars"))
            .Ensure(
                email => !email.StartsWith("admin"),
                _ => new ValidationError("admin addresses not allowed"));

        Console.WriteLine($"    \"hello@example.com\" → 3x Ensure → IsSuccess={result.IsSuccess}");

        var badResult = Result<string, IError>.Success("ab")
            .Ensure(
                email => email.Contains('@'),
                _ => new ValidationError("Must contain @"))
            .Ensure(
                email => email.Length >= 5,
                _ => new ValidationError("Must be at least 5 chars"));

        Console.WriteLine($"    \"ab\" → Ensure(@) fails → IsFailure={badResult.IsFailure}");
        Console.WriteLine($"      Error: \"{((ValidationError)badResult.Error).Message}\"");
        Console.WriteLine($"      Note: second Ensure is skipped (railway — failure propagates)");
        Console.WriteLine();
    }

    private static void ShowImplicitConversion()
    {
        Console.WriteLine("  10.3 Implicit conversions — clean return syntax");
        Console.WriteLine("  ---------------------------------------------------");

        // Simulating a service method
        Result<string, IError> GetUserEmail(bool exists)
        {
            if (!exists)
                return new ValidationError("User not found"); // Implicit error → Result

            return "alice@example.com"; // Implicit value → Result
        }

        var success = GetUserEmail(true);
        var failure = GetUserEmail(false);

        Console.WriteLine($"    GetUserEmail(true):  \"{success.Value}\"");
        Console.WriteLine($"    GetUserEmail(false): Error.Code=\"{failure.Error.Code}\"");
        Console.WriteLine();
        Console.WriteLine("    No explicit Result.Success() / Result.Failure() needed.");
        Console.WriteLine("    The implicit operator handles it.");
        Console.WriteLine();
    }
}
