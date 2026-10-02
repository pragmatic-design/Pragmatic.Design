using Pragmatic.Mapping.Converters;

namespace Showcase.Billing.Infrastructure.Converters;

/// <summary>
/// Converts a decimal amount into a formatted money string.
/// Demonstrates: IValueConverter for custom mapping logic.
/// Used with [MapConverter&lt;MoneyToStringConverter&gt;] on DTO properties.
/// </summary>
public sealed class MoneyToStringConverter : IValueConverter<decimal, string>
{
    public string Convert(decimal source) => source.ToString("N2");

    public decimal ConvertBack(string target) => decimal.TryParse(target, out var result) ? result : 0m;
}
