// =============================================================================
// Railway-Oriented Programming (Map/Bind chain)
// =============================================================================

using Pragmatic.Result.Samples.Errors;

namespace Pragmatic.Result.Samples.Samples;

public static class RailwayOrientedSample
{
    public static void Run()
    {
        Console.WriteLine("2. Railway-Oriented Programming");
        Console.WriteLine("--------------------------------");

        Console.WriteLine($"Valid input: {ParseAndValidate("  hello  ").Match(v => v, e => e.Message)}");
        Console.WriteLine($"Empty input: {ParseAndValidate("   ").Match(v => v, e => e.Message)}");
        Console.WriteLine();
    }

    private static Result<string, ValidationError> ParseAndValidate(string input)
    {
        return Result<string, ValidationError>.Success(input)
            .Map(s => s.Trim())
            .Bind(s => s.Length > 0
                ? Result<string, ValidationError>.Success(s)
                : new ValidationError("Input cannot be empty"))
            .Map(s => s.ToUpperInvariant());
    }
}