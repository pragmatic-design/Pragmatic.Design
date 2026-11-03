using System.Globalization;

namespace Pragmatic.Persistence.Documents.Conversion;

/// <summary>
///     The stored text of a day and of a time of day: the Sql engine's text forms (its SQLite and Oracle columns), <c>yyyy-MM-dd</c> and
///     <c>HH:mm:ss.fffffff</c>. Fixed width and most significant first, so the text sorts and compares as the value does: a filter or an ordering on
///     the field means what it means in .NET. A <c>TimeSpan</c> is its 64-bit ticks, which a number already orders.
/// </summary>
internal static class DocTimeText
{
    private const string DayFormat = "yyyy-MM-dd";
    private const string ClockFormat = "HH:mm:ss.fffffff";

    public static string Day(DateOnly value) => value.ToString(DayFormat, CultureInfo.InvariantCulture);

    public static string Clock(TimeOnly value) => value.ToString(ClockFormat, CultureInfo.InvariantCulture);

    public static DateOnly ParseDay(string text) => DateOnly.ParseExact(text, DayFormat, CultureInfo.InvariantCulture);

    public static TimeOnly ParseClock(string text) => TimeOnly.ParseExact(text, ClockFormat, CultureInfo.InvariantCulture);
}
