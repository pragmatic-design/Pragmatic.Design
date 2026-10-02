using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     Demonstrates I18N configuration with the provider system.
///     Shows how to configure multi-level culture resolution.
/// </summary>
public static class I18NConfigProviderSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("I18N CONFIGURATION PROVIDER SYSTEM");
        Console.WriteLine("   Multi-level culture resolution with priority-based merging");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Scenario A: Static Defaults (Simple Blog/Website)
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("SCENARIO A: Static Defaults (Simple Sites)");
        Console.WriteLine("------------------------------------------");

        var simpleConfig = new I18NConfig
        {
            DefaultUICulture = CultureCode.Italian,
            DefaultDataCulture = CultureCode.Italian,
            SupportedCultures = [CultureCode.Italian, CultureCode.English],
            SyncScopes = true
        };

        I18NContext.SetFromConfig(simpleConfig);

        Console.WriteLine($"  UI Culture: {I18N.UI.Code}");
        Console.WriteLine($"  Data Culture: {I18N.Data.Code}");
        Console.WriteLine($"  Currency: {I18N.Currency.Code}");
        Console.WriteLine($"  SyncScopes: {I18NContext.Current.SyncScopes}");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Scenario B: Enterprise (Separate UI/Data)
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("SCENARIO B: Enterprise (Separate UI/Data)");
        Console.WriteLine("------------------------------------------");

        var enterpriseConfig = new I18NConfig
        {
            DefaultUICulture = CultureCode.EnglishUS,
            DefaultDataCulture = CultureCode.EnglishUS,
            SupportedCultures = [CultureCode.EnglishUS, CultureCode.Italian, CultureCode.German],
            SyncScopes = false
        };

        I18NContext.SetFromConfig(enterpriseConfig);

        // User preference override (simulating UserConfigProvider)
        I18NContext.SetCulture(CultureCode.Italian);

        Console.WriteLine($"  UI Culture: {I18N.UI.Code} (user preference)");
        Console.WriteLine($"  Data Culture: {I18N.Data.Code} (fixed for APIs)");
        Console.WriteLine($"  SyncScopes: {I18NContext.Current.SyncScopes}");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Config with Preferred Currency
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("CONFIGURATION WITH PREFERRED CURRENCY");
        Console.WriteLine("------------------------------------------");

        var fullConfig = new I18NConfig
        {
            DefaultUICulture = CultureCode.GermanSwitzerland,
            DefaultDataCulture = CultureCode.EnglishUS,
            PreferredCurrency = CurrencyCode.CHF,  // Override default EUR
            SyncScopes = false
        };

        I18NContext.SetFromConfig(fullConfig);

        Console.WriteLine($"  UI Culture: {I18N.UI.Code}");
        Console.WriteLine($"  Currency: {I18N.Currency.Code} (CHF override, not EUR)");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Provider Priority Concept
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("PROVIDER PRIORITY CONCEPT");
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("  Priority 0:   SystemConfigProvider (from appsettings.json)");
        Console.WriteLine("  Priority 100: TenantConfigProvider (from tenant DB)");
        Console.WriteLine("  Priority 200: UserConfigProvider (from user preferences)");
        Console.WriteLine("  Priority 300: RequestConfigProvider (Accept-Language header)");
        Console.WriteLine();
        Console.WriteLine("  Higher priority providers override lower ones.");
        Console.WriteLine("  If a provider returns null for a field, it defers to lower priority.");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Custom Scopes in Config
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("CUSTOM SCOPES IN CONFIG");
        Console.WriteLine("------------------------------------------");

        var configWithScopes = new I18NConfig
        {
            DefaultUICulture = CultureCode.English,
            DefaultDataCulture = CultureCode.EnglishUS,
            CustomScopes = new Dictionary<string, CultureCode>
            {
                ["invoicing"] = CultureCode.German,
                ["reporting"] = CultureCode.FrenchFrance
            }
        };

        I18NContext.SetFromConfig(configWithScopes);

        Console.WriteLine($"  UI: {I18N.UI.Code}");
        Console.WriteLine($"  Scope['invoicing']: {I18N.Scope["invoicing"].Code}");
        Console.WriteLine($"  Scope['reporting']: {I18N.Scope["reporting"].Code}");
        Console.WriteLine();

        // Cleanup
        I18NContext.Clear();
    }
}
