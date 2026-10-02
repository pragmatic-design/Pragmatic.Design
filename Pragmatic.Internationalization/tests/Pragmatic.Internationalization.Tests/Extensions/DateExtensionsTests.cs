using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Extensions;

namespace Pragmatic.Internationalization.Tests.Extensions;

[Collection("I18nContext")]
public class DateExtensionsTests
{
    private static readonly DateTime Sample = new(2024, 3, 7, 14, 30, 0);

    [Fact]
    public void FormatDate_EnUs_UsesMonthDayYearOrder()
    {
        I18NContext.Clear();
        I18NContext.SetCulture("en-US");

        var result = Sample.FormatDate();

        result.Should().Be("3/7/2024");
    }

    [Fact]
    public void FormatDate_DeDe_UsesDayMonthYearOrder()
    {
        I18NContext.Clear();
        I18NContext.SetCulture("de-DE");

        var result = Sample.FormatDate();

        result.Should().Be("07.03.2024");
    }

    [Fact]
    public void FormatDateLong_DateOnly_EnUs_ContainsFullMonthName()
    {
        I18NContext.Clear();
        I18NContext.SetCulture("en-US");

        var result = new DateOnly(2024, 3, 7).FormatDateLong();

        result.Should().Contain("March").And.Contain("2024");
    }

    [Fact]
    public void FormatTime_EnUs_ContainsHourAndMinute()
    {
        I18NContext.Clear();
        I18NContext.SetCulture("en-US");

        var result = Sample.FormatTime();

        result.Should().Contain("2:30");
    }

    [Fact]
    public void FormatDateTime_EnUs_ContainsDateAndTime()
    {
        I18NContext.Clear();
        I18NContext.SetCulture("en-US");

        var result = Sample.FormatDateTime();

        result.Should().Contain("2024").And.Contain("2:30");
    }
}
