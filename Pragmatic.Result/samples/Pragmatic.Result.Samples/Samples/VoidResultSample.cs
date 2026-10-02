// =============================================================================
// VoidResult for operations without return value
// =============================================================================

using Pragmatic.Result.Samples.Errors;

namespace Pragmatic.Result.Samples.Samples;

public static class VoidResultSample
{
    public static void Run()
    {
        Console.WriteLine("4. VoidResult for void operations");
        Console.WriteLine("----------------------------------");

        Console.WriteLine($"Age 25: {(ValidateAge(25).IsSuccess ? "Valid" : "Invalid")}");
        Console.WriteLine($"Age -5: {ValidateAge(-5).Match(() => "Valid", e => e.Message)}");
        Console.WriteLine();
    }

    private static VoidResult<ValidationError> ValidateAge(int age)
    {
        if (age < 0)
            return new ValidationError("Age cannot be negative");
        if (age > 150)
            return new ValidationError("Age is unrealistic");
        return VoidResult<ValidationError>.Success();
    }
}