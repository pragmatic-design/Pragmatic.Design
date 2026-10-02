using Pragmatic.Internationalization.Context;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     Demonstrates strongly-typed translation keys with embedded LocalizedString values.
/// </summary>
/// <remarks>
///     <para>
///         The T class is generated from translations/*.json by the source generator.
///         Each key is a LocalizedString with all translations embedded at compile-time.
///     </para>
///     <para>
///         Benefits:
///         - Type-safe keys with IntelliSense
///         - Compile-time error if key is removed
///         - No runtime file loading required
///         - Culture-aware value access
///     </para>
/// </remarks>
public static class StronglyTypedKeysSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("STRONGLY-TYPED TRANSLATION KEYS (Embedded LocalizedString)");
        Console.WriteLine("   Generated from translations/*.json");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // The T class contains LocalizedString properties with embedded translations
        // No provider needed - values are baked in at compile time!

        // Access translations with current culture
        Console.WriteLine("1. Current Culture Access (T.Key.Value):");
        I18NContext.SetCulture("en");
        Console.WriteLine("   Culture: en");
        Console.WriteLine($"   T.Welcome.Value: {T.Welcome.Value}");
        Console.WriteLine($"   T.Goodbye.Value: {T.Goodbye.Value}");
        Console.WriteLine();

        I18NContext.SetCulture("it");
        Console.WriteLine("   Culture: it");
        Console.WriteLine($"   T.Welcome.Value: {T.Welcome.Value}");
        Console.WriteLine($"   T.Goodbye.Value: {T.Goodbye.Value}");
        Console.WriteLine();

        // Access specific culture using indexer
        Console.WriteLine("2. Specific Culture Access (T.Key[\"culture\"]):");
        Console.WriteLine($"   T.Welcome[\"en\"]: {T.Welcome["en"]}");
        Console.WriteLine($"   T.Welcome[\"it\"]: {T.Welcome["it"]}");
        Console.WriteLine();

        // Nested keys become nested classes
        Console.WriteLine("3. Nested Keys (T.Errors.Validation.*):");
        Console.WriteLine($"   T.Errors.Validation.Required[\"en\"]: {T.Errors.Validation.Required["en"]}");
        Console.WriteLine($"   T.Errors.Validation.Required[\"it\"]: {T.Errors.Validation.Required["it"]}");
        Console.WriteLine($"   T.Errors.Validation.Email[\"en\"]: {T.Errors.Validation.Email["en"]}");
        Console.WriteLine($"   T.Errors.Validation.Email[\"it\"]: {T.Errors.Validation.Email["it"]}");
        Console.WriteLine();

        // UI components
        Console.WriteLine("4. UI Keys (T.Buttons.*):");
        Console.WriteLine($"   T.Buttons.Submit[\"en\"]: {T.Buttons.Submit["en"]}");
        Console.WriteLine($"   T.Buttons.Submit[\"it\"]: {T.Buttons.Submit["it"]}");
        Console.WriteLine($"   T.Buttons.Cancel[\"en\"]: {T.Buttons.Cancel["en"]}");
        Console.WriteLine($"   T.Buttons.Cancel[\"it\"]: {T.Buttons.Cancel["it"]}");
        Console.WriteLine();

        // User section
        Console.WriteLine("5. User Keys (T.User.*):");
        Console.WriteLine($"   T.User.Login[\"en\"]: {T.User.Login["en"]}");
        Console.WriteLine($"   T.User.Login[\"it\"]: {T.User.Login["it"]}");
        Console.WriteLine($"   T.User.Logout[\"en\"]: {T.User.Logout["en"]}");
        Console.WriteLine($"   T.User.Logout[\"it\"]: {T.User.Logout["it"]}");
        Console.WriteLine();

        // Implicit string conversion
        Console.WriteLine("6. Implicit String Conversion:");
        I18NContext.SetCulture("en");
        string welcomeEn = T.Welcome; // Uses current culture (en)
        Console.WriteLine($"   string welcome = T.Welcome; // {welcomeEn}");

        I18NContext.SetCulture("it");
        string welcomeIt = T.Welcome; // Uses current culture (it)
        Console.WriteLine($"   (after SetCulture(\"it\")): {welcomeIt}");
        Console.WriteLine();

        // LocalizedString features
        Console.WriteLine("7. LocalizedString Features:");
        var welcome = T.Welcome;
        Console.WriteLine($"   Cultures: {string.Join(", ", welcome.Cultures)}");
        Console.WriteLine($"   Count: {welcome.Count}");
        Console.WriteLine($"   IsEmpty: {welcome.IsEmpty}");
        Console.WriteLine();

        // Benefits summary
        Console.WriteLine("8. Benefits of Embedded LocalizedString:");
        Console.WriteLine("   + No runtime file loading - translations embedded at compile time");
        Console.WriteLine("   + Type-safe keys with full IntelliSense");
        Console.WriteLine("   + Compile-time error if key is removed from JSON");
        Console.WriteLine("   + Culture-aware via I18nContext");
        Console.WriteLine("   + Direct indexer access for specific cultures");
        Console.WriteLine("   + Implicit conversion to string");
        Console.WriteLine();

        // Reset culture
        I18NContext.SetCulture("en");
    }
}