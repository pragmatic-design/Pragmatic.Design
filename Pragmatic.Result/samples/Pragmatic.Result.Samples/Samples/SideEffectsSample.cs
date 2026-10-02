using Pragmatic.Result.Extensions;
using Pragmatic.Result.Samples.Errors;

namespace Pragmatic.Result.Samples.Samples;

/// <summary>
///     Side effects: Match for sync branching, TapAsync/OnSuccessAsync/OnFailureAsync for async.
/// </summary>
public static class SideEffectsSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("9. Side Effects — Match, TapAsync, OnSuccessAsync");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  9.1 Match (void) — sync side effect branching");
        Console.WriteLine("  ------------------------------------------------");

        var success = Result<int, IError>.Success(42);
        success.Match(
            value => Console.WriteLine($"    [Success] Got value: {value}"),
            error => Console.WriteLine($"    [Failure] Error: {error.Code}"));

        var failure = Result<int, IError>.Failure(
            new ValidationError("Age must be 18+"));
        failure.Match(
            value => Console.WriteLine($"    [Success] Got value: {value}"),
            error => Console.WriteLine($"    [Failure] Error: {error.Code}"));
        Console.WriteLine();

        Console.WriteLine("  9.2 Ensure — guard pipeline with inline validation");
        Console.WriteLine("  ----------------------------------------------------");

        var tooYoung = Result<int, IError>.Success(15)
            .Ensure(
                age => age >= 18,
                _ => new ValidationError("Must be 18+"));
        Console.WriteLine($"    Success(15).Ensure(>=18): IsFailure={tooYoung.IsFailure}");

        var oldEnough = Result<int, IError>.Success(25)
            .Ensure(
                age => age >= 18,
                _ => new ValidationError("Must be 18+"));
        Console.WriteLine($"    Success(25).Ensure(>=18): IsSuccess={oldEnough.IsSuccess}, Value={oldEnough.Value}");
        Console.WriteLine();

        Console.WriteLine("  9.3 Async side effects pattern");
        Console.WriteLine("  ---------------------------------");
        Console.WriteLine("""
            // TapAsync — execute side effect on success (e.g., logging)
            await GetUserAsync(id)
                .TapAsync(user => LogAsync($"Found: {user.Name}"))
                .OnFailureAsync(err => AlertAsync(err.Code));

            // OnSuccessAsync — alias for TapAsync
            // OnFailureAsync — execute on failure only
        """);
        Console.WriteLine();
    }
}
