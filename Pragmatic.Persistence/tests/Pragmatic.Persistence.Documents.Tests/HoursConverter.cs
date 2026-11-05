using Pragmatic.Mapping.Converters;

namespace PragmaticDocuments.Tests;

/// <summary>A stay's length as hours, the text <see cref="StayLabel" /> shows: a <c>[MapConverter]</c> of a declared projection (#537).</summary>
public sealed class HoursConverter : IValueConverter<TimeSpan, string>
{
    public string Convert(TimeSpan source) => source.TotalHours.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "h";

    public TimeSpan ConvertBack(string target) => TimeSpan.FromHours(double.Parse(target.TrimEnd('h'), System.Globalization.CultureInfo.InvariantCulture));
}
