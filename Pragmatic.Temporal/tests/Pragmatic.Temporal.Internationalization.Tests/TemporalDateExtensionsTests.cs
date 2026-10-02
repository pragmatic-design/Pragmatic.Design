using System.Globalization;
using Pragmatic.Temporal.Internationalization.Extensions;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Internationalization.Tests;

/// <summary>
///     Ambient-culture extensions read the I18N context, which falls back to
///     <see cref="CultureInfo.CurrentCulture" /> — set per test and restored.
/// </summary>
public class TemporalDateExtensionsTests
{
    private static readonly LocalDateTime SampleLocal = new(2026, 3, 21, 14, 30, 45);
    private static readonly DateTimeOffset SampleInstant = new(2026, 1, 15, 23, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("it-IT")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void FormatDateTime_LocalDateTime_UsesAmbientCulture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);

        var result = RunWithCulture(culture, () => SampleLocal.FormatDateTime());

        Assert.Equal(SampleLocal.ToDateTime().ToString("g", culture), result);
    }

    [Theory]
    [InlineData("it-IT")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void FormatDate_And_FormatTime_LocalDateTime_UseAmbientCulture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);

        var date = RunWithCulture(culture, () => SampleLocal.FormatDate());
        var time = RunWithCulture(culture, () => SampleLocal.FormatTime());

        Assert.Equal(SampleLocal.ToDateTime().ToString("d", culture), date);
        Assert.Equal(SampleLocal.ToDateTime().ToString("t", culture), time);
    }

    [Fact]
    public void FormatDateTimeLong_LocalDateTime_PreservesWallComponents()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");

        var result = RunWithCulture(culture, () => SampleLocal.FormatDateTimeLong());

        Assert.Equal(SampleLocal.ToDateTime().ToString("F", culture), result);
    }

    [Theory]
    [InlineData("it-IT")]
    [InlineData("en-US")]
    public void FormatDateTime_ZonedDateTime_UsesWallTimeInItsOwnZone(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var rome = ZonedDateTime.FromUtc(SampleInstant, "Europe/Rome");

        var result = RunWithCulture(culture, () => rome.FormatDateTime());

        Assert.Equal(rome.ToDateTimeOffset().ToString("g", culture), result);
    }

    [Fact]
    public void Format_ZonedDateTime_SameInstantDifferentZones_Differs()
    {
        var culture = CultureInfo.GetCultureInfo("it-IT");
        var rome = ZonedDateTime.FromUtc(SampleInstant, "Europe/Rome");
        var utc = ZonedDateTime.FromUtc(SampleInstant, TimeZoneInfo.Utc);

        var romeOut = RunWithCulture(culture, () => rome.FormatDateTime());
        var utcOut = RunWithCulture(culture, () => utc.FormatDateTime());

        Assert.NotEqual(utcOut, romeOut);
    }

    [Fact]
    public void Format_ZonedDateTime_AllFourMembers_DelegateToDateTimeOffset()
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");
        var rome = ZonedDateTime.FromUtc(SampleInstant, "Europe/Rome");
        var dto = rome.ToDateTimeOffset();

        RunWithCulture(culture, () =>
        {
            Assert.Equal(dto.ToString("d", culture), rome.FormatDate());
            Assert.Equal(dto.ToString("t", culture), rome.FormatTime());
            Assert.Equal(dto.ToString("g", culture), rome.FormatDateTime());
            Assert.Equal(dto.ToString("F", culture), rome.FormatDateTimeLong());
            return string.Empty;
        });
    }

    private static string RunWithCulture(CultureInfo culture, Func<string> action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
