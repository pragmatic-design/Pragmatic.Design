using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Extensions;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Tests.Extensions;

[Collection("I18nContext")]
public class MoneyExtensionsTests
{
    [Fact]
    public void ToMoney_DecimalWithCurrency_CreatesMoney()
    {
        var money = 42.50m.ToMoney(CurrencyCode.USD);

        money.Amount.Should().Be(42.50m);
        money.Currency.Code.Should().Be("USD");
    }

    [Fact]
    public void ToMoney_IntWithCurrency_CreatesMoney()
    {
        var money = 5.ToMoney(CurrencyCode.EUR);

        money.Amount.Should().Be(5m);
        money.Currency.Code.Should().Be("EUR");
    }

    [Fact]
    public void ToMoney_DecimalUsesContextCurrency()
    {
        I18NContext.Clear();
        I18NContext.SetCulture("en-US");

        var money = 99.99m.ToMoney();

        money.Amount.Should().Be(99.99m);
        money.Currency.Should().Be(I18NContext.Current.Currency);
    }

    [Fact]
    public void Format_WithCultureName_HonorsCultureSeparators()
    {
        var money = Money.From(1234.56m, CurrencyCode.EUR);

        var result = money.Format("it-IT");

        result.Should().Contain("1.234,56").And.Contain("€");
    }

    [Fact]
    public void Format_WithCultureCode_HonorsCulture()
    {
        var money = Money.From(1000m, CurrencyCode.USD);

        var result = money.Format(CultureCode.FromString("en-US"));

        result.Should().Contain("$").And.Contain("1,000");
    }

    [Fact]
    public void FormatOrDefault_Null_ReturnsDefaultValue()
    {
        Money? money = null;

        money.FormatOrDefault("n/a").Should().Be("n/a");
    }

    [Fact]
    public void FormatOrDefault_HasValue_ReturnsFormatted()
    {
        I18NContext.Clear();
        I18NContext.SetCulture("en-US");
        Money? money = Money.From(5m, CurrencyCode.USD);

        money.FormatOrDefault().Should().Contain("5");
    }

    [Fact]
    public void RoundToCurrency_RoundsToMinorUnits()
    {
        var money = Money.From(10.999m, CurrencyCode.USD);

        var rounded = money.RoundToCurrency();

        rounded.Amount.Should().Be(11.00m);
    }
}
