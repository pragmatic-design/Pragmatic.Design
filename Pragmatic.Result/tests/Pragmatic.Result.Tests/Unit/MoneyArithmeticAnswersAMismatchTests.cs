using Pragmatic.Internationalization.Types;
using Pragmatic.Result.Monetary;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

/// <summary>
///     <c>Money.Add</c>, <c>Subtract</c> and <c>Compare</c> answer two currencies with <see cref="CurrencyMismatchError" />
///     instead of throwing (#643).
/// </summary>
/// <remarks>
///     The operators throw on a mismatch, which reached the host as a 500 for an amount that came from a request.
///     These are the path for such amounts; the operators keep throwing, for code that knows its currencies agree.
/// </remarks>
public class MoneyArithmeticAnswersAMismatchTests
{
    private static readonly Money TenEuro = Money.From(10m, CurrencyCode.EUR);
    private static readonly Money FourEuro = Money.From(4m, CurrencyCode.EUR);
    private static readonly Money FiveDollars = Money.From(5m, CurrencyCode.USD);

    [Fact]
    public void Add_InOneCurrency_IsTheSum()
        => TenEuro.Add(FourEuro).Value.Should().Be(Money.From(14m, CurrencyCode.EUR));

    [Fact]
    public void Subtract_InOneCurrency_IsTheDifference()
        => TenEuro.Subtract(FourEuro).Value.Should().Be(Money.From(6m, CurrencyCode.EUR));

    [Fact]
    public void Compare_InOneCurrency_OrdersTheAmounts()
        => TenEuro.Compare(FourEuro).Value.Should().BePositive();

    [Fact]
    public void Add_InTwoCurrencies_IsAMismatchNamingBoth()
    {
        var result = TenEuro.Add(FiveDollars);

        result.IsFailure.Should().BeTrue();
        result.Error.Left.Should().Be("EUR");
        result.Error.Right.Should().Be("USD");
        result.Error.Code.Should().Be("CURRENCY_MISMATCH");
        result.Error.StatusCode.Should().Be(422);
    }

    [Fact]
    public void Subtract_InTwoCurrencies_IsAMismatch()
        => TenEuro.Subtract(FiveDollars).IsFailure.Should().BeTrue();

    [Fact]
    public void Compare_InTwoCurrencies_IsAMismatch()
        => TenEuro.Compare(FiveDollars).IsFailure.Should().BeTrue();

    /// <summary>The control: the operator keeps throwing, as the programmer-error path it is.</summary>
    [Fact]
    public void TheOperator_StillThrows()
        => Assert.Throws<InvalidOperationException>(() => TenEuro + FiveDollars);
}
