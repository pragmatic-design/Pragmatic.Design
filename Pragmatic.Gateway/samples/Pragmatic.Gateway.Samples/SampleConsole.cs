namespace Pragmatic.Gateway.Samples;

/// <summary>
///     Tiny console helpers so every sample prints a consistent, readable header and key/value lines.
/// </summary>
internal static class SampleConsole
{
    public static void Header(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 72));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('=', 72));
    }

    public static void Section(string title) => Console.WriteLine($"\n-- {title} --");

    public static void Item(string label, object? value) => Console.WriteLine($"  {label,-30} : {value}");

    public static void Note(string text) => Console.WriteLine($"  - {text}");
}
