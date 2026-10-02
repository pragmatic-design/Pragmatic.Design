using System.Globalization;
using Pragmatic.Internationalization.Formatting;
using Pragmatic.Temporal.Internationalization.Formatting;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Internationalization.Tests;

public class GlobalizationFormatterTemporalExtensionsTests
{
    private static readonly LocalDateTime SampleLocal = new(2026, 3, 21, 14, 30, 45);

    // 2026-01-15T23:30Z → Europe/Rome (UTC+1 in January) = 2026-01-16T00:30 wall time:
    // date AND time differ from the UTC view of the same instant.
    private static readonly DateTimeOffset SampleInstant = new(2026, 1, 15, 23, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("it-IT")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void FormatDate_LocalDateTime_MatchesCultureShortDatePattern(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var formatter = new GlobalizationFormatter(culture);

        var result = formatter.FormatDate(SampleLocal);

        Assert.Equal(SampleLocal.ToDateTime().ToString("d", culture), result);
    }

    [Theory]
    [InlineData("it-IT")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void FormatTime_LocalDateTime_MatchesCultureShortTimePattern(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var formatter = new GlobalizationFormatter(culture);

        var result = formatter.FormatTime(SampleLocal);

        Assert.Equal(SampleLocal.ToDateTime().ToString("t", culture), result);
    }

    [Theory]
    [InlineData("it-IT")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void FormatDateTime_LocalDateTime_MatchesCultureGeneralPattern(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var formatter = new GlobalizationFormatter(culture);

        var result = formatter.FormatDateTime(SampleLocal);

        Assert.Equal(SampleLocal.ToDateTime().ToString("g", culture), result);
    }

    [Theory]
    [InlineData("it-IT")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void FormatDateTimeLong_LocalDateTime_MatchesCultureFullPattern(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var formatter = new GlobalizationFormatter(culture);

        var result = formatter.FormatDateTimeLong(SampleLocal);

        Assert.Equal(SampleLocal.ToDateTime().ToString("F", culture), result);
    }

    [Theory]
    [InlineData("it-IT")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void FormatDateTime_ZonedDateTime_UsesWallTimeInItsOwnZone(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var formatter = new GlobalizationFormatter(culture);
        var rome = ZonedDateTime.FromUtc(SampleInstant, "Europe/Rome");

        var result = formatter.FormatDateTime(rome);

        Assert.Equal(rome.ToDateTimeOffset().ToString("g", culture), result);
    }

    [Fact]
    public void FormatDateTime_SameInstantDifferentZones_ProducesDifferentOutput()
    {
        var formatter = new GlobalizationFormatter(CultureInfo.GetCultureInfo("it-IT"));
        var rome = ZonedDateTime.FromUtc(SampleInstant, "Europe/Rome");
        var utc = ZonedDateTime.FromUtc(SampleInstant, TimeZoneInfo.Utc);

        // 23:30Z is 00:30 on the NEXT day in Rome: both date and time differ.
        Assert.NotEqual(formatter.FormatDateTime(utc), formatter.FormatDateTime(rome));
        Assert.NotEqual(formatter.FormatDate(utc), formatter.FormatDate(rome));
        Assert.NotEqual(formatter.FormatTime(utc), formatter.FormatTime(rome));
    }

    [Fact]
    public void FormatDate_ZonedDateTime_MatchesZoneWallDate()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var formatter = new GlobalizationFormatter(culture);
        var rome = ZonedDateTime.FromUtc(SampleInstant, "Europe/Rome");

        var result = formatter.FormatDate(rome);

        // Wall date in Rome is January 16th, not the UTC 15th.
        Assert.Equal(rome.ToDateTimeOffset().ToString("d", culture), result);
        Assert.Contains("16", result);
    }

    [Fact]
    public void FormatDate_LocalDate_StillWorksViaImplicitConversion()
    {
        // Sanity: the bridge is not needed for LocalDate/LocalTime — implicit
        // DateOnly/TimeOnly conversions reach the existing formatter overloads.
        var culture = CultureInfo.GetCultureInfo("de-DE");
        var formatter = new GlobalizationFormatter(culture);
        var date = new LocalDate(2026, 3, 21);
        var time = new LocalTime(14, 30);

        Assert.Equal(new DateOnly(2026, 3, 21).ToString("d", culture), formatter.FormatDate(date));
        Assert.Equal(new TimeOnly(14, 30).ToString("t", culture), formatter.FormatTime(time));
    }
}
