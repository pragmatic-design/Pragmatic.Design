using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Ensure.Samples.Samples;

/// <summary>
///     Collection guards, GUID checks, date checks, and the Is-pattern for non-throwing validation.
/// </summary>
public static class CollectionAndMiscSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("5. Collection, Date & Misc Guards");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowCollectionGuards();
        ShowDateAndEnumGuards();
        ShowIsPatternVariety();
        ShowFluentReturn();

        Console.WriteLine();
    }

    private static void ShowCollectionGuards()
    {
        Console.WriteLine("  5.1 Collection guards — arrays, lists, IEnumerable");
        Console.WriteLine("  ----------------------------------------------------");

        // Non-null, non-empty collection
        int[] items = [1, 2, 3];
        ThrowIfNull(items);
        Console.WriteLine("    ThrowIfNull([1,2,3]) — OK");

        List<string> names = ["Alice", "Bob"];
        ThrowIfNull(names);
        Console.WriteLine("    ThrowIfNull([\"Alice\",\"Bob\"]) — OK");

        try { ThrowIfNull<int[]>(null!); }
        catch (ArgumentNullException ex) { Console.WriteLine($"    ThrowIfNull(null array) → {ex.GetType().Name}"); }

        Console.WriteLine();
    }

    private static void ShowDateAndEnumGuards()
    {
        Console.WriteLine("  5.2 Date, enum, and equality guards");
        Console.WriteLine("  ----------------------------------------");

        // Date guards
        Console.WriteLine($"    IsPast(yesterday):    {IsPast(DateTime.UtcNow.AddDays(-1))}");
        Console.WriteLine($"    IsFuture(tomorrow):   {IsFuture(DateTime.UtcNow.AddDays(1))}");
        Console.WriteLine($"    IsNotDefault(now):    {IsNotDefault(DateTime.UtcNow)}");
        Console.WriteLine($"    IsNotDefault(default):{IsNotDefault(default(DateTime))}");

        // Enum guards
        Console.WriteLine($"    IsDefined(Monday):    {IsDefined(DayOfWeek.Monday)}");
        Console.WriteLine($"    IsDefined((DayOfWeek)99): {IsDefined((DayOfWeek)99)}");

        // Equality guards
        Console.WriteLine($"    AreEqual(5, 5):       {AreEqual(5, 5)}");
        Console.WriteLine($"    AreEqual(5, 3):       {AreEqual(5, 3)}");
        Console.WriteLine($"    AreNotEqual(5, 3):    {AreNotEqual(5, 3)}");
        Console.WriteLine();
    }

    private static void ShowIsPatternVariety()
    {
        Console.WriteLine("  5.2 Is-pattern — non-throwing conditional checks");
        Console.WriteLine("  ---------------------------------------------------");

        Console.WriteLine($"    IsNotNull(\"hello\"):      {IsNotNull("hello")}");
        Console.WriteLine($"    IsNotNull(null):         {IsNotNull<string>(null)}");
        Console.WriteLine($"    IsNotNullOrEmpty(\"hi\"):  {IsNotNullOrEmpty("hi")}");
        Console.WriteLine($"    IsNotNullOrEmpty(\"\"):    {IsNotNullOrEmpty("")}");
        Console.WriteLine($"    IsEmail(\"a@b.com\"):      {IsEmail("a@b.com")}");
        Console.WriteLine($"    IsEmail(\"nope\"):         {IsEmail("nope")}");
        Console.WriteLine($"    IsPositive(42):          {IsPositive(42)}");
        Console.WriteLine($"    IsPositive(-1):          {IsPositive(-1)}");
        Console.WriteLine($"    IsInRange(25, 18, 120):  {IsInRange(25, 18, 120)}");
        Console.WriteLine($"    IsInRange(200, 0, 100):  {IsInRange(200, 0, 100)}");
        Console.WriteLine();

        Console.WriteLine("    Is-methods return bool — use in if/when/switch.");
        Console.WriteLine("    ThrowIf-methods throw — use in constructors/guards.");
        Console.WriteLine();
    }

    private static void ShowFluentReturn()
    {
        Console.WriteLine("  5.3 Fluent return — ThrowIfNull returns the validated value");
        Console.WriteLine("  ---------------------------------------------------------------");

        Console.WriteLine("""
            // ThrowIfNull returns T — enables assignment chaining:
            public class UserService
            {
                private readonly IRepository _repo;
                private readonly ILogger _logger;

                public UserService(IRepository repo, ILogger logger)
                {
                    _repo = Ensure.ThrowIfNull(repo);    // Assigns + validates
                    _logger = Ensure.ThrowIfNull(logger); // One line per field
                }
            }
        """);
        Console.WriteLine();
    }
}
