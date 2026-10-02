namespace Pragmatic.Ensure.Samples.Samples;

/// <summary>
///     Demonstrates Is pattern for conditional logic without throwing exceptions.
/// </summary>
public static class ConditionalLogicSample
{
    public static void Run()
    {
        Console.WriteLine("--- Conditional Logic Sample (Is Pattern) ---\n");

        // String validation
        ValidateStrings();

        // Numeric validation
        ValidateNumbers();

        // Collection validation
        ValidateCollections();

        // Date validation
        ValidateDates();

        Console.WriteLine();
    }

    private static void ValidateStrings()
    {
        string?[] inputs = ["hello", "", null, "   ", "test@example.com"];

        Console.WriteLine("String validations:");
        foreach (var input in inputs)
        {
            var display = input is null ? "null" : $"\"{input}\"";
            Console.WriteLine($"  {display}:");
            Console.WriteLine($"    IsNotNull: {Ensure.IsNotNull(input)}");
            Console.WriteLine($"    IsNotNullOrEmpty: {Ensure.IsNotNullOrEmpty(input)}");
            Console.WriteLine($"    IsNotNullOrWhiteSpace: {Ensure.IsNotNullOrWhiteSpace(input)}");
            Console.WriteLine($"    IsEmail: {(!string.IsNullOrEmpty(input) ? Ensure.IsEmail(input) : false)}");
        }
    }

    private static void ValidateNumbers()
    {
        int[] numbers = [-5, 0, 5, 50, 150];

        Console.WriteLine("\nNumeric validations (range 0-100):");
        foreach (var n in numbers)
        {
            Console.WriteLine($"  {n}:");
            Console.WriteLine($"    IsPositive: {Ensure.IsPositive(n)}");
            Console.WriteLine($"    IsNegative: {Ensure.IsNegative(n)}");
            Console.WriteLine($"    IsZero: {Ensure.IsZero(n)}");
            Console.WriteLine($"    IsInRange(0,100): {Ensure.IsInRange(n, 0, 100)}");
        }
    }

    private static void ValidateCollections()
    {
        Console.WriteLine("\nCollection validations:");

        int[]? nullArray = null;
        int[] emptyArray = [];
        int[] filledArray = [1, 2, 3];

        Console.WriteLine($"  null array - IsNotNullOrEmpty: {Ensure.IsNotNullOrEmpty(nullArray)}");
        Console.WriteLine($"  empty array - IsNotNullOrEmpty: {Ensure.IsNotNullOrEmpty(emptyArray)}");
        Console.WriteLine($"  [1,2,3] - IsNotNullOrEmpty: {Ensure.IsNotNullOrEmpty(filledArray)}");
    }

    private static void ValidateDates()
    {
        Console.WriteLine("\nDate validations:");

        var past = DateTime.UtcNow.AddDays(-1);
        var future = DateTime.UtcNow.AddDays(1);
        var defaultDate = default(DateTime);

        Console.WriteLine($"  Yesterday - IsPast: {Ensure.IsPast(past)}, IsFuture: {Ensure.IsFuture(past)}");
        Console.WriteLine($"  Tomorrow - IsPast: {Ensure.IsPast(future)}, IsFuture: {Ensure.IsFuture(future)}");
        Console.WriteLine($"  Default - IsNotDefault: {Ensure.IsNotDefault(defaultDate)}");
    }
}