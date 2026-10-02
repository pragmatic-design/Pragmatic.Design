// =============================================================================
// TryCatch pattern for bridging exception-based code
// =============================================================================

using Pragmatic.Result.Http;
using Pragmatic.Result.Samples.Errors;

namespace Pragmatic.Result.Samples.Samples;

public static class TryCatchSample
{
    public static void Run()
    {
        Console.WriteLine("7. TryCatch Pattern for Legacy Integration");
        Console.WriteLine("-------------------------------------------");

        // Bridge exception-throwing legacy code with the real Result.Try factory.
        var result1 = Result.Try(() => GetDataFromLegacy(1), ex => BadRequestError.Create(ex.Message));
        var result2 = Result.Try(() => GetDataFromLegacy(999), ex => BadRequestError.Create(ex.Message));

        Console.WriteLine($"Legacy call 1: {result1.Match(d => $"Got data: {d}", e => $"Error: {e.Reason}")}");
        Console.WriteLine($"Legacy call 2: {result2.Match(d => $"Got data: {d}", e => $"Error: {e.Reason}")}");

        // Using Result with validation
        var validated = ValidateAndProcess("test@email.com");
        Console.WriteLine($"Validated email: {validated.Match(v => v, e => $"Invalid: {e.Message}")}");
        Console.WriteLine();
    }

    // Simulates a legacy method that throws
    private static string GetDataFromLegacy(int id)
    {
        if (id > 100)
            throw new InvalidOperationException($"Item {id} not found in legacy system");
        return $"Data for {id}";
    }

    // Example of validation with Result
    private static Result<string, ValidationError> ValidateAndProcess(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return new ValidationError("Email cannot be empty");

        if (!email.Contains('@'))
            return new ValidationError("Email must contain @");

        return email.ToLowerInvariant();
    }
}