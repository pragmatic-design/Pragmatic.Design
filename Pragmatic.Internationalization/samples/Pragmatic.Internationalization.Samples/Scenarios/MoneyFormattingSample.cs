using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     Demonstrates Money type and currency formatting across cultures.
/// </summary>
public static class MoneyFormattingSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("2. MONEY & CURRENCY FORMATTING");
        Console.WriteLine("   Culture-aware monetary value handling");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // Creating Money values
        Console.WriteLine("Creating Money values:");
        var usd = Money.From(1234.56m, CurrencyCode.USD);
        var eur = Money.From(1234.56m, CurrencyCode.EUR);
        var jpy = Money.From(12345m, CurrencyCode.JPY); // No decimals

        Console.WriteLine($"  USD: {usd}");
        Console.WriteLine($"  EUR: {eur}");
        Console.WriteLine($"  JPY: {jpy}");
        Console.WriteLine();

        // Currency properties
        Console.WriteLine("Currency properties:");
        Console.WriteLine($"  CurrencyCode.USD.Code: {CurrencyCode.USD.Code}");
        Console.WriteLine($"  CurrencyCode.USD.Name: {CurrencyCode.USD.Name}");
        Console.WriteLine($"  CurrencyCode.USD.Symbol: {CurrencyCode.USD.Symbol}");
        Console.WriteLine($"  CurrencyCode.USD.MinorUnits: {CurrencyCode.USD.MinorUnits}");
        Console.WriteLine($"  CurrencyCode.JPY.MinorUnits: {CurrencyCode.JPY.MinorUnits} (no decimals)");
        Console.WriteLine();

        // Money arithmetic
        Console.WriteLine("Money arithmetic:");
        var price = Money.From(100m, CurrencyCode.EUR);
        var tax = Money.From(22m, CurrencyCode.EUR);
        var total = price + tax;
        Console.WriteLine($"  {price} + {tax} = {total}");

        var doubled = price * 2;
        Console.WriteLine($"  {price} * 2 = {doubled}");
        Console.WriteLine();

        // Money from cents (using decimal division)
        Console.WriteLine("Money from cents:");
        var fromCents = Money.From(123.45m, CurrencyCode.USD); // Equivalent to 12345 cents
        Console.WriteLine($"  Money.From(123.45m, USD): {fromCents}");
        Console.WriteLine($"  Amount: {fromCents.Amount}");
        Console.WriteLine();

        // Culture-specific formatting
        Console.WriteLine("Culture-specific formatting:");
        var amount = Money.From(1234567.89m, CurrencyCode.EUR);

        I18NContext.SetCulture("en-US");
        Console.WriteLine($"  en-US: {amount.Format(I18NContext.Current.Culture)}");

        I18NContext.SetCulture("de-DE");
        Console.WriteLine($"  de-DE: {amount.Format(I18NContext.Current.Culture)}");

        I18NContext.SetCulture("it-IT");
        Console.WriteLine($"  it-IT: {amount.Format(I18NContext.Current.Culture)}");

        I18NContext.SetCulture("fr-FR");
        Console.WriteLine($"  fr-FR: {amount.Format(I18NContext.Current.Culture)}");
        Console.WriteLine();

        // Currency lookup
        Console.WriteLine("Currency lookup:");
        var gbp = CurrencyCode.FromCode("GBP");
        Console.WriteLine($"  FromCode('GBP'): {gbp.Name} ({gbp.Symbol})");

        var isValid = CurrencyCode.IsValid("CHF");
        Console.WriteLine($"  IsValid('CHF'): {isValid}");

        CurrencyCode.TryFromCode("XYZ", out var invalid);
        Console.WriteLine($"  TryFromCode('XYZ'): {(invalid == default ? "not found" : invalid.Code)}");
        Console.WriteLine();

        // Reset culture
        I18NContext.SetCulture("en");
    }
}