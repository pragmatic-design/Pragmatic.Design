// =============================================================================
// Deconstruction pattern
// =============================================================================

using Pragmatic.Result.Samples.Errors;

namespace Pragmatic.Result.Samples.Samples;

public static class DeconstructSample
{
    public static void Run()
    {
        Console.WriteLine("6. Deconstruction pattern");
        Console.WriteLine("--------------------------");

        var result = Result<int, ValidationError>.Success(42);
        var (isSuccess, val, error) = result;
        Console.WriteLine($"IsSuccess: {isSuccess}, Value: {val}");
        Console.WriteLine();
    }
}