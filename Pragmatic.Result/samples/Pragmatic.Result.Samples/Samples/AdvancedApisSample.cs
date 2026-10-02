// =============================================================================
// Advanced APIs — Recover, OrElse, CollectAll, Combine, Try, FromNullable
// =============================================================================

using Pragmatic.Result.Extensions;
using Pragmatic.Result.Http;
using Pragmatic.Result.Samples.Errors;

namespace Pragmatic.Result.Samples.Samples;

/// <summary>
///     Exercises the real recovery, aggregation and factory APIs from the package:
///     Recover / OrElse (sync), CollectAll + AggregateError, Combine, Result.Try and
///     Result.FromNullable.
/// </summary>
public static class AdvancedApisSample
{
    public static void Run()
    {
        Console.WriteLine("11. Advanced APIs");
        Console.WriteLine("-----------------");

        ShowRecoverAndOrElse();
        ShowCollectAll();
        ShowCombine();
        ShowTryAndFromNullable();

        Console.WriteLine();
    }

    private static void ShowRecoverAndOrElse()
    {
        Console.WriteLine("  11.1 Recover / OrElse");

        Result<int, NotFoundError> missing = NotFoundError.Create("Counter", 1);

        // Recover — supply a fallback VALUE, turning failure into success
        var recovered = missing.Recover(_ => -1);
        Console.WriteLine($"    Recover  → IsSuccess={recovered.IsSuccess}, Value={recovered.Value}");

        // OrElse — supply a fallback RESULT (could itself fail)
        var orElse = missing.OrElse(_ => Result<int, NotFoundError>.Success(0));
        Console.WriteLine($"    OrElse   → IsSuccess={orElse.IsSuccess}, Value={orElse.Value}");
    }

    private static void ShowCollectAll()
    {
        Console.WriteLine("  11.2 CollectAll → AggregateError (accumulates ALL errors)");

        var results = new[]
        {
            ValidateName("Alice"),
            ValidateName(""),
            ValidateName("Bob"),
            ValidateName("   ")
        };

        var collected = results.CollectAll();
        if (collected.IsFailure)
        {
            Console.WriteLine($"    {collected.Error.Count} error(s) collected (Code={collected.Error.Code}):");
            foreach (var error in collected.Error.Errors)
                Console.WriteLine($"      - {error.Code}");
        }

        var allValid = new[] { ValidateName("Alice"), ValidateName("Bob") }.CollectAll();
        Console.WriteLine($"    All valid → IsSuccess={allValid.IsSuccess}, count={allValid.Value.Count}");
    }

    private static void ShowCombine()
    {
        Console.WriteLine("  11.3 Combine (fail-fast, tuple of values)");

        var combined = ResultExtensions.Combine(ValidateName("Alice"), ValidateName("Bob"));
        Console.WriteLine($"    Combine(ok, ok)   → {combined.Match(t => $"({t.Item1}, {t.Item2})", e => e.Code)}");

        var combinedFail = ResultExtensions.Combine(ValidateName("Alice"), ValidateName(""));
        Console.WriteLine($"    Combine(ok, fail) → {combinedFail.Match(t => $"({t.Item1}, {t.Item2})", e => e.Code)}");
    }

    private static void ShowTryAndFromNullable()
    {
        Console.WriteLine("  11.4 Result.Try / Result.FromNullable");

        // Result.Try — bridge exception-throwing code
        var parsed = Result.Try(
            () => int.Parse("not-a-number", System.Globalization.CultureInfo.InvariantCulture),
            ex => new ValidationError(ex.Message));
        Console.WriteLine($"    Try(parse bad)      → IsFailure={parsed.IsFailure}");

        // Result.FromNullable — bridge a nullable lookup, lazy error factory
        string? found = "cached-value";
        string? missing = null;

        var hit = Result.FromNullable(found, () => NotFoundError.Create("Cache", "k1"));
        var miss = Result.FromNullable(missing, () => NotFoundError.Create("Cache", "k2"));
        Console.WriteLine($"    FromNullable(value) → IsSuccess={hit.IsSuccess}, Value={hit.Value}");
        Console.WriteLine($"    FromNullable(null)  → IsFailure={miss.IsFailure}, Error={miss.Error.Code}");
    }

    private static Result<string, ValidationError> ValidateName(string name)
        => string.IsNullOrWhiteSpace(name)
            ? new ValidationError("Name cannot be empty")
            : name;
}
