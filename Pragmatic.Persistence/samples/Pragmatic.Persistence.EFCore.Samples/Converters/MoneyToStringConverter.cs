using Pragmatic.Mapping.Converters;
using Pragmatic.Persistence.EFCore.Samples.ValueObjects;

namespace Pragmatic.Persistence.EFCore.Samples.Converters;

/// <summary>
///     Converts Money value object to/from string representation.
///     Demonstrates custom converter usage with [MapConverter].
/// </summary>
public class MoneyToStringConverter : IValueConverter<Money, string>
{
    public string Convert(Money source) => source.ToString();
    public Money ConvertBack(string target) => Money.Parse(target);
}

/// <summary>
///     Converts Money to decimal (amount only, loses currency).
///     Useful when only the amount is needed.
/// </summary>
public class MoneyToDecimalConverter : IValueConverter<Money, decimal>
{
    public decimal Convert(Money source) => source.Amount;
    public Money ConvertBack(decimal target) => new(target);
}
