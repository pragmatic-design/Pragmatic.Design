using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     Demonstrates I18NContext - the unified multi-scope culture context.
/// </summary>
public static class CultureContextSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("1. MULTI-SCOPE CULTURE CONTEXT");
        Console.WriteLine("   I18NContext provides unified culture management with scopes");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Type-Safe Culture Codes
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("Type-Safe Culture Codes:");
        Console.WriteLine($"  CultureCode.Italian: {CultureCode.Italian.Code}");
        Console.WriteLine($"  CultureCode.EnglishUS: {CultureCode.EnglishUS.Code}");
        Console.WriteLine($"  CultureCode.GermanSwitzerland: {CultureCode.GermanSwitzerland.Code}");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // UI and Data Cultures (Multi-Scope)
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("UI and Data Cultures (Multi-Scope):");

        // Configure with separate UI and Data cultures
        var config = new I18NConfig
        {
            DefaultUICulture = CultureCode.Italian,
            DefaultDataCulture = CultureCode.EnglishUS,
            SyncScopes = false
        };
        I18NContext.SetFromConfig(config);

        Console.WriteLine($"  I18N.UI: {I18N.UI.Code} (user interface)");
        Console.WriteLine($"  I18N.Data: {I18N.Data.Code} (APIs, storage)");
        Console.WriteLine();

        // Change UI culture independently
        I18NContext.SetCulture(CultureCode.German);
        Console.WriteLine($"After SetCulture(German):");
        Console.WriteLine($"  I18N.UI: {I18N.UI.Code}");
        Console.WriteLine($"  I18N.Data: {I18N.Data.Code} (unchanged)");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Custom Scopes
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("Custom Scopes:");
        I18NContext.SetScope("invoicing", CultureCode.French);
        I18NContext.SetScope("reporting", CultureCode.Japanese);

        Console.WriteLine($"  I18N.Scope[\"invoicing\"]: {I18N.Scope["invoicing"].Code}");
        Console.WriteLine($"  I18N.Scope[\"reporting\"]: {I18N.Scope["reporting"].Code}");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Currency from Culture
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("Currency Integration:");
        I18NContext.SetCulture(CultureCode.Italian);
        Console.WriteLine($"  I18N.UI: {I18N.UI.Code}");
        Console.WriteLine($"  I18N.Currency: {I18N.Currency.Code} (from Italian culture)");

        var price = I18N.CreateMoney(1234.56m);
        Console.WriteLine($"  I18N.CreateMoney(1234.56m): {price.Amount} {price.Currency.Code}");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Scoped Execution
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("Scoped Execution (WithCulture):");
        I18NContext.SetCulture(CultureCode.EnglishUS);
        Console.WriteLine($"  Current: {I18N.UI.Code}");

        var germanResult = I18NContext.WithCulture(CultureCode.German, () =>
        {
            Console.WriteLine($"  Inside WithCulture: {I18N.UI.Code}");
            return I18N.Currency.Code;
        });

        Console.WriteLine($"  German currency: {germanResult}");
        Console.WriteLine($"  Back outside: {I18N.UI.Code}");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // SyncScopes Mode (Simple Sites)
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("SyncScopes Mode (for simple monolingua sites):");

        // When using SetCulture directly (not via SetFromConfig), SyncScopes=true
        I18NContext.Clear();
        I18NContext.SetCulture(CultureCode.Italian);
        Console.WriteLine($"  SetCulture(Italian) with SyncScopes=true:");
        Console.WriteLine($"    I18N.UI = I18N.Data: {I18N.UI.Code == I18N.Data.Code}");
        Console.WriteLine($"    Both: {I18N.UI.Code}");
        Console.WriteLine();
    }
}
