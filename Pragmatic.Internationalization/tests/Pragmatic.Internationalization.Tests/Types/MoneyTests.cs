using System.Globalization;
using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Types;

[Collection("I18nContext")]
public class MoneyTests
{
    [Fact]
    public void From_WithDecimalAndCurrency_CreatesMoney()
    {
        // Act
        var money = Money.From(99.99m, CurrencyCode.USD);

        // Assert
        money.Amount.Should().Be(99.99m);
        money.Currency.Should().Be(CurrencyCode.USD);
    }

    [Fact]
    public void From_WithStringCurrency_CreatesMoney()
    {
        // Act
        var money = Money.From(50.00m, "EUR");

        // Assert
        money.Amount.Should().Be(50.00m);
        money.Currency.Code.Should().Be("EUR");
    }

    [Fact]
    public void Zero_ReturnsZeroAmount()
    {
        // Act
        var money = Money.Zero(CurrencyCode.GBP);

        // Assert
        money.Amount.Should().Be(0m);
        money.Currency.Should().Be(CurrencyCode.GBP);
        money.IsZero.Should().BeTrue();
    }

    [Fact]
    public void TryFrom_WithValidCurrency_ReturnsTrue()
    {
        // Act
        var result = Money.TryFrom(100m, "JPY", out var money);

        // Assert
        result.Should().BeTrue();
        money.Amount.Should().Be(100m);
        money.Currency.Code.Should().Be("JPY");
    }

    [Fact]
    public void TryFrom_WithInvalidCurrency_ReturnsFalse()
    {
        // Act
        var result = Money.TryFrom(100m, "INVALID", out var money);

        // Assert
        result.Should().BeFalse();
        money.Should().Be(default);
    }

    [Fact]
    public void IsPositive_WhenPositive_ReturnsTrue()
    {
        // Arrange
        var money = Money.From(10m, CurrencyCode.USD);

        // Assert
        money.IsPositive.Should().BeTrue();
        money.IsNegative.Should().BeFalse();
        money.IsZero.Should().BeFalse();
    }

    [Fact]
    public void IsNegative_WhenNegative_ReturnsTrue()
    {
        // Arrange
        var money = Money.From(-10m, CurrencyCode.USD);

        // Assert
        money.IsNegative.Should().BeTrue();
        money.IsPositive.Should().BeFalse();
    }

    [Fact]
    public void Abs_ReturnsAbsoluteValue()
    {
        // Arrange
        var money = Money.From(-50m, CurrencyCode.EUR);

        // Act
        var abs = money.Abs();

        // Assert
        abs.Amount.Should().Be(50m);
    }

    // Arithmetic Operations

    [Fact]
    public void Addition_SameCurrency_Works()
    {
        // Arrange
        var a = Money.From(10m, CurrencyCode.USD);
        var b = Money.From(20m, CurrencyCode.USD);

        // Act
        var result = a + b;

        // Assert
        result.Amount.Should().Be(30m);
        result.Currency.Should().Be(CurrencyCode.USD);
    }

    [Fact]
    public void Addition_DifferentCurrency_Throws()
    {
        // Arrange
        var usd = Money.From(10m, CurrencyCode.USD);
        var eur = Money.From(10m, CurrencyCode.EUR);

        // Act & Assert
        var action = () => usd + eur;
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*different currencies*");
    }

    [Fact]
    public void Subtraction_SameCurrency_Works()
    {
        // Arrange
        var a = Money.From(30m, CurrencyCode.EUR);
        var b = Money.From(10m, CurrencyCode.EUR);

        // Act
        var result = a - b;

        // Assert
        result.Amount.Should().Be(20m);
    }

    [Fact]
    public void Multiplication_ByFactor_Works()
    {
        // Arrange
        var money = Money.From(10m, CurrencyCode.GBP);

        // Act
        var result1 = money * 3;
        var result2 = 3 * money;

        // Assert
        result1.Amount.Should().Be(30m);
        result2.Amount.Should().Be(30m);
    }

    [Fact]
    public void Division_ByFactor_Works()
    {
        // Arrange
        var money = Money.From(100m, CurrencyCode.JPY);

        // Act
        var result = money / 4;

        // Assert
        result.Amount.Should().Be(25m);
    }

    [Fact]
    public void UnaryNegation_Works()
    {
        // Arrange
        var money = Money.From(50m, CurrencyCode.USD);

        // Act
        var result = -money;

        // Assert
        result.Amount.Should().Be(-50m);
    }

    // Comparison Operations

    [Fact]
    public void Comparison_SameCurrency_Works()
    {
        // Arrange
        var small = Money.From(10m, CurrencyCode.USD);
        var large = Money.From(20m, CurrencyCode.USD);

        // Assert
        (small < large).Should().BeTrue();
        (large > small).Should().BeTrue();
        (small <= large).Should().BeTrue();
        (large >= small).Should().BeTrue();
    }

    [Fact]
    public void CompareTo_SameCurrency_ReturnsCorrectOrder()
    {
        // Arrange
        var small = Money.From(10m, CurrencyCode.EUR);
        var large = Money.From(20m, CurrencyCode.EUR);

        // Assert
        small.CompareTo(large).Should().BeLessThan(0);
        large.CompareTo(small).Should().BeGreaterThan(0);
    }

    // Rounding

    [Fact]
    public void Round_ToSpecifiedDecimals_Works()
    {
        // Arrange
        var money = Money.From(10.456m, CurrencyCode.USD);

        // Act
        var result = money.Round(2);

        // Assert
        result.Amount.Should().Be(10.46m);
    }

    [Fact]
    public void RoundToMinorUnit_UseCurrencyDecimals()
    {
        // Arrange
        var usd = Money.From(10.999m, CurrencyCode.USD); // 2 decimals
        var jpy = Money.From(10.999m, CurrencyCode.JPY); // 0 decimals

        // Act
        var roundedUsd = usd.RoundToMinorUnit();
        var roundedJpy = jpy.RoundToMinorUnit();

        // Assert
        roundedUsd.Amount.Should().Be(11.00m);
        roundedJpy.Amount.Should().Be(11m);
    }

    // Formatting

    [Fact]
    public void Format_WithCulture_FormatsCorrectly()
    {
        // Arrange
        var money = Money.From(1234.56m, CurrencyCode.EUR);
        var italian = CultureInfo.GetCultureInfo("it-IT");

        // Act
        var formatted = money.Format(italian);

        // Assert - Italian uses . for thousands and , for decimals
        formatted.Should().Contain("1.234,56");
        formatted.Should().Contain("€");
    }

    [Fact]
    public void Format_UsesCurrentContext()
    {
        // Arrange
        var money = Money.From(1000m, CurrencyCode.USD);
        I18NContext.SetCulture("en-US");

        // Act
        var formatted = money.Format();

        // Assert
        formatted.Should().Contain("$");
        formatted.Should().Contain("1,000");
    }

    // Equality

    [Fact]
    public void Equality_SameAmountAndCurrency_AreEqual()
    {
        // Arrange
        var a = Money.From(100m, CurrencyCode.USD);
        var b = Money.From(100m, CurrencyCode.USD);

        // Assert
        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
        (a != b).Should().BeFalse();
    }

    [Fact]
    public void Equality_DifferentAmount_NotEqual()
    {
        // Arrange
        var a = Money.From(100m, CurrencyCode.USD);
        var b = Money.From(200m, CurrencyCode.USD);

        // Assert
        (a == b).Should().BeFalse();
    }

    [Fact]
    public void Equality_DifferentCurrency_NotEqual()
    {
        // Arrange
        var a = Money.From(100m, CurrencyCode.USD);
        var b = Money.From(100m, CurrencyCode.EUR);

        // Assert
        (a == b).Should().BeFalse();
    }

    // Deconstruction

    [Fact]
    public void Deconstruct_ExtractsComponents()
    {
        // Arrange
        var money = Money.From(99.99m, CurrencyCode.GBP);

        // Act
        var (amount, currency) = money;

        // Assert
        amount.Should().Be(99.99m);
        currency.Should().Be(CurrencyCode.GBP);
    }

    [Fact]
    public void ToString_ReturnsInvariantFormat()
    {
        // Arrange
        var money = Money.From(1234.56m, CurrencyCode.EUR);

        // Act
        var str = money.ToString();

        // Assert
        str.Should().Be("1234.56 EUR");
    }
}