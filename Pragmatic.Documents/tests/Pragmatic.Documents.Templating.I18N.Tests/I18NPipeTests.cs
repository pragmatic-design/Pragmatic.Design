using System.Globalization;
using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Templating.I18N;

namespace Pragmatic.Documents.Templating.I18N.Tests;

public class I18NPipeTests
{
    // --- DatePipe ---

    [Fact]
    public void DatePipe_FormatsDateTimeOffset()
    {
        var pipe = new DatePipe();
        var date = new DateTimeOffset(2026, 4, 9, 10, 30, 0, TimeSpan.Zero);

        var result = pipe.Execute(date, ["dd/MM/yyyy"], CultureInfo.InvariantCulture);

        result.Should().Be("09/04/2026");
    }

    [Fact]
    public void DatePipe_FormatsDateTime()
    {
        var pipe = new DatePipe();
        var date = new DateTime(2026, 12, 25);

        var result = pipe.Execute(date, ["yyyy-MM-dd"], CultureInfo.InvariantCulture);

        result.Should().Be("2026-12-25");
    }

    [Fact]
    public void DatePipe_FormatsDateOnly()
    {
        var pipe = new DatePipe();
        var date = new DateOnly(2026, 1, 15);

        var result = pipe.Execute(date, ["MMMM dd"], CultureInfo.GetCultureInfo("en-US"));

        result.Should().Be("January 15");
    }

    [Fact]
    public void DatePipe_ItalianLocale()
    {
        var pipe = new DatePipe();
        var date = new DateTimeOffset(2026, 4, 9, 0, 0, 0, TimeSpan.Zero);

        var result = pipe.Execute(date, ["dd MMMM yyyy"], CultureInfo.GetCultureInfo("it-IT"));

        result!.ToString().Should().Contain("aprile");
    }

    [Fact]
    public void DatePipe_DefaultFormat_ShortDate()
    {
        var pipe = new DatePipe();
        var date = new DateTimeOffset(2026, 4, 9, 0, 0, 0, TimeSpan.Zero);

        var result = pipe.Execute(date, [], CultureInfo.GetCultureInfo("en-US"));

        result.Should().NotBeNull();
        result!.ToString().Should().Contain("4/9/2026");
    }

    [Fact]
    public void DatePipe_Null_ReturnsNull()
    {
        var pipe = new DatePipe();
        pipe.Execute(null, ["dd/MM/yyyy"], CultureInfo.InvariantCulture).Should().BeNull();
    }

    // --- CurrencyPipe ---

    [Fact]
    public void CurrencyPipe_FormatsEur()
    {
        var pipe = new CurrencyPipe();

        var result = pipe.Execute(1234.50, ["EUR"], CultureInfo.GetCultureInfo("it-IT"));

        result!.ToString().Should().Contain("€");
        result.ToString().Should().Contain("1.234,50");
    }

    [Fact]
    public void CurrencyPipe_FormatsUsd()
    {
        var pipe = new CurrencyPipe();

        var result = pipe.Execute(99.99, ["USD"], CultureInfo.GetCultureInfo("en-US"));

        result!.ToString().Should().Contain("$");
        result.ToString().Should().Contain("99.99");
    }

    [Fact]
    public void CurrencyPipe_NoCurrencyCode_UsesLocaleDefault()
    {
        var pipe = new CurrencyPipe();

        var result = pipe.Execute(50, [], CultureInfo.GetCultureInfo("en-US"));

        result!.ToString().Should().Contain("$");
    }

    [Fact]
    public void CurrencyPipe_FromDecimal()
    {
        var pipe = new CurrencyPipe();

        var result = pipe.Execute(1500.75m, ["EUR"], CultureInfo.GetCultureInfo("de-DE"));

        result!.ToString().Should().Contain("€");
    }

    [Fact]
    public void CurrencyPipe_Null_ReturnsNull()
    {
        var pipe = new CurrencyPipe();
        pipe.Execute(null, ["EUR"], CultureInfo.InvariantCulture).Should().BeNull();
    }

    // --- PercentPipe ---

    [Fact]
    public void PercentPipe_FormatsPercentage()
    {
        var pipe = new PercentPipe();

        var result = pipe.Execute(0.425, ["2"], CultureInfo.GetCultureInfo("en-US"));

        result!.ToString().Should().Contain("42.50");
        result.ToString().Should().Contain("%");
    }

    [Fact]
    public void PercentPipe_NoDecimals()
    {
        var pipe = new PercentPipe();

        var result = pipe.Execute(0.75, [], CultureInfo.GetCultureInfo("en-US"));

        result!.ToString().Should().Contain("75");
        result.ToString().Should().Contain("%");
    }

    [Fact]
    public void PercentPipe_ItalianLocale()
    {
        var pipe = new PercentPipe();

        var result = pipe.Execute(0.333, ["1"], CultureInfo.GetCultureInfo("it-IT"));

        result.Should().NotBeNull();
        result!.ToString().Should().Contain("33");
    }

    [Fact]
    public void PercentPipe_Null_ReturnsNull()
    {
        var pipe = new PercentPipe();
        pipe.Execute(null, [], CultureInfo.InvariantCulture).Should().BeNull();
    }
}
