using System.Text.Json;
using Pragmatic.Internationalization.AspNetCore.Json.Converters;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     Demonstrates the System.Text.Json converters that let the I18N value types
///     (<see cref="Money"/>, <see cref="CurrencyCode"/>, <see cref="LocalizedString"/>)
///     round-trip cleanly through JSON request/response payloads.
/// </summary>
public static class JsonConvertersSample
{
    private sealed record Product(string Sku, Money Price, CurrencyCode PreferredCurrency, LocalizedString Title);

    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("JSON CONVERTERS");
        Console.WriteLine("   Money / CurrencyCode / LocalizedString round-trip via System.Text.Json");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters =
            {
                new MoneyJsonConverter(),
                new CurrencyCodeJsonConverter(),
                new LocalizedStringJsonConverter(),
            },
        };

        var title = new LocalizedString();
        title.Set("en-US", "Wireless Mouse");
        title.Set("it-IT", "Mouse senza fili");

        var product = new Product(
            Sku: "MX-100",
            Price: Money.From(29.95m, CurrencyCode.FromCode("EUR")),
            PreferredCurrency: CurrencyCode.FromCode("USD"),
            Title: title);

        var json = JsonSerializer.Serialize(product, options);
        Console.WriteLine("Serialized product:");
        Console.WriteLine(json);
        Console.WriteLine();

        // Deserialize the Money and CurrencyCode portions back to verify round-trip fidelity.
        var moneyJson = JsonSerializer.Serialize(product.Price, options);
        var money = JsonSerializer.Deserialize<Money>(moneyJson, options);
        Console.WriteLine($"  Money round-trip: {money.Amount} {money.Currency.Code}");

        var currencyJson = JsonSerializer.Serialize(product.PreferredCurrency, options);
        var currency = JsonSerializer.Deserialize<CurrencyCode>(currencyJson, options);
        Console.WriteLine($"  CurrencyCode round-trip: {currency.Code}");

        Console.WriteLine();
    }
}
