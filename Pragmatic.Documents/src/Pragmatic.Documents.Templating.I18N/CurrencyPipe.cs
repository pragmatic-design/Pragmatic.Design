using System.Globalization;
using Pragmatic.Documents.Templating.Pipes;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Documents.Templating.I18N;

/// <summary>
/// Locale-aware currency formatting pipe: <c>{{amount | currency:"EUR"}}</c>, or <c>{{total | currency}}</c>
/// over a <see cref="Money" />, which brings its own currency.
/// With neither a code nor a Money, formats with the culture's own currency format.
/// </summary>
public sealed class CurrencyPipe : ITemplatePipe
{
    public string Name => "currency";

    public object? Execute(object? input, IReadOnlyList<string> args, CultureInfo culture)
    {
        if (input is null) return null;

        var amount = ToDecimal(input);
        if (amount is null) return input.ToString();

        // A Money carries its currency, so an invoice in the customer's currency says which one; a code
        // written in the template still wins, being the author's last word.
        var currencyCode = args.Count > 0
            ? args[0].ToUpperInvariant()
            : input is Money money ? money.Currency.Code : null;

        if (currencyCode is not null)
        {

            // Use the culture's currency format ("C") so symbol PLACEMENT and grouping
            // follow the locale convention (prefix vs. suffix). Only the symbol glyph and
            // decimal digits are overridden per ISO currency code. Clone NumberFormatInfo
            // so the shared culture instance is never mutated (thread-safe).
            var nfi = (NumberFormatInfo)culture.NumberFormat.Clone();
            nfi.CurrencySymbol = SymbolFor(currencyCode);
            if (DecimalsFor(currencyCode) is { } digits)
                nfi.CurrencyDecimalDigits = digits;

            return amount.Value.ToString("C", nfi);
        }

        // No currency code: use culture's currency format
        return amount.Value.ToString("C", culture);
    }

    private static string SymbolFor(string currencyCode) => currencyCode switch
    {
        "EUR" => "€",
        "USD" => "$",
        "GBP" => "£",
        "JPY" => "¥",
        "CHF" => "CHF",
        _ => currencyCode
    };

    /// <summary>ISO 4217 minor-unit overrides; <c>null</c> keeps the culture default (2).</summary>
    private static int? DecimalsFor(string currencyCode) => currencyCode switch
    {
        "JPY" => 0,
        _ => null
    };

    private static decimal? ToDecimal(object? value) => value switch
    {
        decimal d => d,
        Money money => money.Amount,
        double dbl => (decimal)dbl,
        float f => (decimal)f,
        int i => i,
        long l => l,
        string s when decimal.TryParse(s, CultureInfo.InvariantCulture, out var d) => d,
        _ => null
    };
}
