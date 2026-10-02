using System.Globalization;

namespace TimeOff.IntegrationTests.Infrastructure;

/// <summary>
///     Dates for requests that must not have started: February of next year.
/// </summary>
/// <remarks>
///     Deciding and withdrawing read the application's clock, and a date written into a test would one
///     day be in the past. February holds no Italian public holiday, so the days from its first Monday
///     are working days: <c>Day(0)</c>…<c>Day(4)</c> is a working week.
/// </remarks>
public static class TestCalendar
{
    public static readonly DateOnly FirstMondayOfFebruary = FirstMondayOfFebruaryNextYear();

    public static int Year => FirstMondayOfFebruary.Year;

    /// <summary>The day <paramref name="offset" /> days after the first Monday of February, as the API writes it.</summary>
    public static string Day(int offset)
        => FirstMondayOfFebruary.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly FirstMondayOfFebruaryNextYear()
    {
        var day = new DateOnly(DateTime.UtcNow.Year + 1, 2, 1);
        while (day.DayOfWeek != DayOfWeek.Monday)
            day = day.AddDays(1);
        return day;
    }
}
