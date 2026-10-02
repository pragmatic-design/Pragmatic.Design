using Pragmatic.Internationalization.Providers;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     Demonstrates localization provider configuration and composition.
/// </summary>
public static class ProviderConfigurationSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("6. PROVIDER CONFIGURATION");
        Console.WriteLine("   Composing multiple localization sources");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // InMemoryLocalizationProvider - for testing and runtime additions
        Console.WriteLine("InMemoryLocalizationProvider:");
        var inMemory = new InMemoryLocalizationProvider()
            .AddString("en", "app.name", "My Application")
            .AddString("en", "app.version", "1.0.0")
            .AddString("it", "app.name", "La Mia Applicazione")
            .AddStrings("de", new Dictionary<string, string>
            {
                ["app.name"] = "Meine Anwendung",
                ["app.version"] = "1.0.0"
            });

        Console.WriteLine($"  Supported cultures: {string.Join(", ", inMemory.SupportedCultures)}");
        Console.WriteLine($"  en: {inMemory.GetString("app.name", "en")}");
        Console.WriteLine($"  it: {inMemory.GetString("app.name", "it")}");
        Console.WriteLine($"  de: {inMemory.GetString("app.name", "de")}");
        Console.WriteLine();

        // Provider priority
        Console.WriteLine("Provider priority (higher = checked first):");
        var lowPriority = new InMemoryLocalizationProvider { Priority = 0 }
            .AddString("en", "shared.key", "From low priority")
            .AddString("en", "low.only", "Only in low priority");

        var highPriority = new InMemoryLocalizationProvider { Priority = 100 }
            .AddString("en", "shared.key", "From high priority")
            .AddString("en", "high.only", "Only in high priority");

        Console.WriteLine($"  Low priority (0): shared.key = {lowPriority.GetString("shared.key", "en")}");
        Console.WriteLine($"  High priority (100): shared.key = {highPriority.GetString("shared.key", "en")}");
        Console.WriteLine();

        // CompositeLocalizationProvider - combining multiple sources
        Console.WriteLine("CompositeLocalizationProvider:");
        var composite = new CompositeLocalizationProvider([lowPriority, highPriority]);

        Console.WriteLine($"  Provider count: {composite.ProviderCount}");
        Console.WriteLine($"  Combined priority: {composite.Priority}");
        Console.WriteLine($"  shared.key (from high): {composite.GetString("shared.key", "en")}");
        Console.WriteLine($"  low.only (from low): {composite.GetString("low.only", "en")}");
        Console.WriteLine($"  high.only (from high): {composite.GetString("high.only", "en")}");
        Console.WriteLine();

        // GetAll - merged dictionary
        Console.WriteLine("GetAll - merged dictionary:");
        var all = composite.GetAll("en");
        Console.WriteLine($"  Total keys: {all.Count}");
        foreach (var kvp in all)
            Console.WriteLine($"    {kvp.Key}: {kvp.Value}");
        Console.WriteLine();

        // Typical composition pattern
        Console.WriteLine("Typical composition pattern:");
        Console.WriteLine("  1. Framework defaults (Priority: 0)");
        Console.WriteLine("  2. App translations (Priority: 50)");
        Console.WriteLine("  3. Runtime overrides (Priority: 100)");
        Console.WriteLine();

        var framework = new InMemoryLocalizationProvider { Priority = 0 }
            .AddString("en", "error.generic", "An error occurred");

        var app = new InMemoryLocalizationProvider { Priority = 50 }
            .AddString("en", "error.generic", "Oops! Something went wrong.")
            .AddString("en", "error.notfound", "Resource not found");

        var runtime = new InMemoryLocalizationProvider { Priority = 100 };
        // Runtime can override at any time
        runtime.AddString("en", "error.generic", "MAINTENANCE: Service temporarily unavailable");

        var combined = new CompositeLocalizationProvider([framework, app, runtime]);
        Console.WriteLine($"  error.generic: {combined.GetString("error.generic", "en")}");
        Console.WriteLine($"  error.notfound: {combined.GetString("error.notfound", "en")}");
        Console.WriteLine();

        // Clearing and removing
        Console.WriteLine("Dynamic operations (InMemory):");
        var dynamic = new InMemoryLocalizationProvider()
            .AddString("en", "temp.key", "Temporary value");
        Console.WriteLine($"  Before remove: {dynamic.GetString("temp.key", "en")}");

        dynamic.RemoveString("en", "temp.key");
        Console.WriteLine($"  After remove: {dynamic.GetString("temp.key", "en") ?? "(null)"}");
        Console.WriteLine();
    }
}