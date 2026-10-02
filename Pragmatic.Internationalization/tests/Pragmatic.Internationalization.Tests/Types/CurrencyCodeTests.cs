using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Types;

public class CurrencyCodeTests
{
    [Fact]
    public void StaticCurrencies_HaveCorrectProperties()
    {
        // Assert - USD
        CurrencyCode.USD.Code.Should().Be("USD");
        CurrencyCode.USD.Name.Should().Be("US Dollar");
        CurrencyCode.USD.Symbol.Should().Be("$");
        CurrencyCode.USD.MinorUnits.Should().Be(2);

        // Assert - EUR
        CurrencyCode.EUR.Code.Should().Be("EUR");
        CurrencyCode.EUR.Name.Should().Be("Euro");
        CurrencyCode.EUR.Symbol.Should().Be("€");
        CurrencyCode.EUR.MinorUnits.Should().Be(2);

        // Assert - JPY (0 decimals)
        CurrencyCode.JPY.Code.Should().Be("JPY");
        CurrencyCode.JPY.MinorUnits.Should().Be(0);
    }

    [Fact]
    public void FromCode_ValidCode_ReturnsCurrency()
    {
        // Act
        var currency = CurrencyCode.FromCode("GBP");

        // Assert
        currency.Code.Should().Be("GBP");
        currency.Name.Should().Be("British Pound");
    }

    [Fact]
    public void FromCode_InvalidCode_ThrowsArgumentException()
    {
        // Act & Assert
        var action = () => CurrencyCode.FromCode("INVALID");
        action.Should().Throw<ArgumentException>()
            .WithMessage("*not a valid ISO 4217 currency code*");
    }

    [Fact]
    public void FromCode_CaseInsensitive_Works()
    {
        // Act
        var lower = CurrencyCode.FromCode("usd");
        var upper = CurrencyCode.FromCode("USD");
        var mixed = CurrencyCode.FromCode("UsD");

        // Assert
        lower.Should().Be(upper);
        lower.Should().Be(mixed);
    }

    [Fact]
    public void TryFromCode_ValidCode_ReturnsTrue()
    {
        // Act
        var result = CurrencyCode.TryFromCode("CHF", out var currency);

        // Assert
        result.Should().BeTrue();
        currency.Code.Should().Be("CHF");
    }

    [Fact]
    public void TryFromCode_InvalidCode_ReturnsFalse()
    {
        // Act
        var result = CurrencyCode.TryFromCode("XYZ", out var currency);

        // Assert
        result.Should().BeFalse();
        currency.Should().Be(default(CurrencyCode));
    }

    [Fact]
    public void TryFromCode_NullOrEmpty_ReturnsFalse()
    {
        // Act & Assert
        CurrencyCode.TryFromCode(null, out _).Should().BeFalse();
        CurrencyCode.TryFromCode("", out _).Should().BeFalse();
    }

    [Fact]
    public void IsValid_ValidCode_ReturnsTrue()
    {
        // Assert
        CurrencyCode.IsValid("USD").Should().BeTrue();
        CurrencyCode.IsValid("eur").Should().BeTrue();
    }

    [Fact]
    public void IsValid_InvalidCode_ReturnsFalse()
    {
        // Assert
        CurrencyCode.IsValid("INVALID").Should().BeFalse();
        CurrencyCode.IsValid(null).Should().BeFalse();
        CurrencyCode.IsValid("").Should().BeFalse();
    }

    [Fact]
    public void All_ContainsCommonCurrencies()
    {
        // Act
        var all = CurrencyCode.All;

        // Assert
        all.Should().NotBeEmpty();
        all.Should().Contain(c => c.Code == "USD");
        all.Should().Contain(c => c.Code == "EUR");
        all.Should().Contain(c => c.Code == "GBP");
        all.Should().Contain(c => c.Code == "JPY");
    }

    [Fact]
    public void Equality_SameCode_AreEqual()
    {
        // Arrange
        var a = CurrencyCode.USD;
        var b = CurrencyCode.FromCode("USD");

        // Assert
        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
        (a != b).Should().BeFalse();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentCode_NotEqual()
    {
        // Assert
        (CurrencyCode.USD == CurrencyCode.EUR).Should().BeFalse();
        (CurrencyCode.USD != CurrencyCode.EUR).Should().BeTrue();
    }

    [Fact]
    public void ImplicitConversion_ToString_Works()
    {
        // Arrange
        string code = CurrencyCode.USD;

        // Assert
        code.Should().Be("USD");
    }

    [Fact]
    public void ToString_ReturnsCode()
    {
        // Assert
        CurrencyCode.EUR.ToString().Should().Be("EUR");
    }
}