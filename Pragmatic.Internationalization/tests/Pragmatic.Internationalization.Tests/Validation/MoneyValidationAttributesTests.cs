using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Attributes;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Tests.Validation;

public class MoneyValidationAttributesTests
{
    private readonly NonNegativeMoneyAttribute _nonNegative = new();
    private readonly PositiveMoneyAttribute _positive = new();

    [Fact]
    public void NonNegative_PositiveAmount_IsValid()
    {
        _nonNegative.IsValid(Money.From(10m, CurrencyCode.USD)).Should().BeTrue();
    }

    [Fact]
    public void NonNegative_ZeroAmount_IsValid()
    {
        _nonNegative.IsValid(Money.From(0m, CurrencyCode.USD)).Should().BeTrue();
    }

    [Fact]
    public void NonNegative_NegativeAmount_IsInvalid()
    {
        _nonNegative.IsValid(Money.From(-1m, CurrencyCode.USD)).Should().BeFalse();
    }

    [Fact]
    public void NonNegative_Null_IsValid()
    {
        _nonNegative.IsValid(null).Should().BeTrue();
    }

    [Fact]
    public void NonNegative_NonMoneyValue_IsInvalid()
    {
        _nonNegative.IsValid("not money").Should().BeFalse();
    }

    [Fact]
    public void Positive_PositiveAmount_IsValid()
    {
        _positive.IsValid(Money.From(0.01m, CurrencyCode.USD)).Should().BeTrue();
    }

    [Fact]
    public void Positive_ZeroAmount_IsInvalid()
    {
        _positive.IsValid(Money.From(0m, CurrencyCode.USD)).Should().BeFalse();
    }

    [Fact]
    public void Positive_NegativeAmount_IsInvalid()
    {
        _positive.IsValid(Money.From(-5m, CurrencyCode.USD)).Should().BeFalse();
    }

    [Fact]
    public void Positive_Null_IsValid()
    {
        _positive.IsValid(null).Should().BeTrue();
    }
}
