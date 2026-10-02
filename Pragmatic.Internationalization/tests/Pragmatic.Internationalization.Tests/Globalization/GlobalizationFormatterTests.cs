using System.Globalization;
using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Formatting;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Tests.Globalization;

public class GlobalizationFormatterTests
{
    [Fact]
    public void Constructor_NullCulture_Throws()
    {
        var act = () => new GlobalizationFormatter((CultureInfo)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Culture_ReflectsConstructorArgument()
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");

        var formatter = new GlobalizationFormatter(culture);

        formatter.Culture.Should().Be(culture);
    }

    [Fact]
    public void FormatNumber_UsesBoundCulture_NotCurrentContext()
    {
        var formatter = new GlobalizationFormatter(CultureInfo.GetCultureInfo("en-US"));

        formatter.FormatNumber(1234.5m).Should().Be("1,234.50");
    }

    [Fact]
    public void FormatNumber_GermanCulture_UsesGermanSeparators()
    {
        var formatter = new GlobalizationFormatter(CultureInfo.GetCultureInfo("de-DE"));

        formatter.FormatNumber(1234.5m).Should().Be("1.234,50");
    }

    [Fact]
    public void FormatInteger_AppliesThousandSeparators()
    {
        var formatter = new GlobalizationFormatter(CultureInfo.GetCultureInfo("en-US"));

        formatter.FormatInteger(1000000).Should().Be("1,000,000");
    }

    [Fact]
    public void FormatMoney_FormatsWithBoundCulture()
    {
        var formatter = new GlobalizationFormatter(CultureInfo.GetCultureInfo("en-US"));

        var result = formatter.FormatMoney(Money.From(9.99m, CurrencyCode.USD));

        result.Should().Contain("9").And.Contain("99").And.Contain("$");
    }

    [Fact]
    public void FormatMoney_AmountAndCurrency_FormatsWithBoundCulture()
    {
        var formatter = new GlobalizationFormatter(CultureInfo.GetCultureInfo("en-US"));

        var result = formatter.FormatMoney(5m, CurrencyCode.USD);

        result.Should().Contain("5").And.Contain("$");
    }

    [Fact]
    public void FormatDate_UsesBoundCulture()
    {
        var formatter = new GlobalizationFormatter(CultureInfo.GetCultureInfo("en-US"));

        var result = formatter.FormatDate(new DateOnly(2024, 1, 31));

        result.Should().Be("1/31/2024");
    }

    [Fact]
    public void FormatPercent_UsesBoundCulture()
    {
        var formatter = new GlobalizationFormatter(CultureInfo.GetCultureInfo("en-US"));

        var result = formatter.FormatPercent(0.5m);

        result.Should().Contain("50").And.Contain("%");
    }

    [Fact]
    public void FormatFileSize_FormatsHumanReadable()
    {
        var formatter = new GlobalizationFormatter(CultureInfo.GetCultureInfo("en-US"));

        formatter.FormatFileSize(0).Should().Be("0 B");
        formatter.FormatFileSize(1536).Should().Contain("KB");
    }
}
