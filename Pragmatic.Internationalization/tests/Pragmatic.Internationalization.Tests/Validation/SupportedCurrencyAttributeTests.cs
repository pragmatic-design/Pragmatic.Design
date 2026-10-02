using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Attributes;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Tests.Validation;

public class SupportedCurrencyAttributeTests
{
    [Fact]
    public void IsValid_MoneyWithSupportedCurrency_IsValid()
    {
        var attribute = new SupportedCurrencyAttribute("USD", "EUR");

        attribute.IsValid(Money.From(1m, CurrencyCode.USD)).Should().BeTrue();
    }

    [Fact]
    public void IsValid_MoneyWithUnsupportedCurrency_IsInvalid()
    {
        var attribute = new SupportedCurrencyAttribute("EUR");

        attribute.IsValid(Money.From(1m, CurrencyCode.USD)).Should().BeFalse();
    }

    [Fact]
    public void IsValid_CurrencyCodeSupported_IsValid()
    {
        var attribute = new SupportedCurrencyAttribute("USD");

        attribute.IsValid(CurrencyCode.USD).Should().BeTrue();
    }

    [Fact]
    public void IsValid_CurrencyCodeUnsupported_IsInvalid()
    {
        var attribute = new SupportedCurrencyAttribute("EUR");

        attribute.IsValid(CurrencyCode.USD).Should().BeFalse();
    }

    [Fact]
    public void IsValid_CaseInsensitiveWhitelist_StillMatches()
    {
        // Whitelist supplied in lowercase; matching is case-insensitive.
        var attribute = new SupportedCurrencyAttribute("usd");

        attribute.IsValid(CurrencyCode.USD).Should().BeTrue();
    }

    [Fact]
    public void IsValid_RawString_IsInvalid()
    {
        // The attribute only inspects Money and CurrencyCode; a bare string is unsupported.
        var attribute = new SupportedCurrencyAttribute("USD");

        attribute.IsValid("USD").Should().BeFalse();
    }

    [Fact]
    public void IsValid_Null_IsValid()
    {
        var attribute = new SupportedCurrencyAttribute("USD");

        attribute.IsValid(null).Should().BeTrue();
    }

    [Fact]
    public void IsValid_UnrelatedType_IsInvalid()
    {
        var attribute = new SupportedCurrencyAttribute("USD");

        attribute.IsValid(123).Should().BeFalse();
    }
}
