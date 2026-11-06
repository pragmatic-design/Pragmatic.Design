using Pragmatic.Mapping.Converters;

namespace PragmaticEntities.Tests;

/// <summary>An amount as the text <see cref="StatementSummary" /> shows: a <c>[MapConverter]</c> of a declared projection (#563).</summary>
public sealed class EuroConverter : IValueConverter<decimal, string>
{
    public string Convert(decimal source) => source.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " EUR";

    public decimal ConvertBack(string target) => decimal.Parse(target.Replace(" EUR", ""), System.Globalization.CultureInfo.InvariantCulture);
}
