// =============================================================================
// Maybe<T> for optional values
// =============================================================================

namespace Pragmatic.Result.Samples.Samples;

public static class MaybeSample
{
    public static void Run()
    {
        Console.WriteLine("5. Maybe<T> for optional values");
        Console.WriteLine("--------------------------------");

        var theme = FindSetting("theme");
        var unknown = FindSetting("unknown");

        Console.WriteLine($"Theme: {theme.GetValueOrDefault("light")}");
        Console.WriteLine($"Unknown: {unknown.GetValueOrDefault("default")}");
        Console.WriteLine($"Has theme: {theme.HasValue}");
        Console.WriteLine($"Has unknown: {unknown.HasValue}");
        Console.WriteLine();
    }

    private static Maybe<string> FindSetting(string key)
    {
        return key switch
        {
            "theme" => Maybe<string>.Some("dark"),
            "language" => Maybe<string>.Some("en-US"),
            _ => Maybe<string>.None()
        };
    }
}