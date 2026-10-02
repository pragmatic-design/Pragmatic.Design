// Pragmatic.Discovery Samples - Console formatting helpers.

namespace Pragmatic.Discovery.Samples;

/// <summary>Small helpers to keep sample output consistent and readable.</summary>
internal static class SampleConsole
{
    public static void Header(string title)
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine(title);
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();
    }

    public static void Step(string text) => Console.WriteLine($"  → {text}");

    public static void Info(string text) => Console.WriteLine($"    {text}");

    public static void Blank() => Console.WriteLine();
}
