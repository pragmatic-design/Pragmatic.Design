using Microsoft.AspNetCore.Http.HttpResults;
using Pragmatic.Internationalization.Types;
using Pragmatic.Result.Monetary;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Result.AspNetCore.Tests;

/// <summary>
///     Two amounts in different currencies, added through <c>Money.Add</c>, reach the caller as 422
///     <c>CURRENCY_MISMATCH</c> naming both currencies, not as a 500 (#643).
/// </summary>
/// <remarks>
///     Through the same <c>ToHttpResult</c> an endpoint's <c>Result</c> takes, with no handler of its own: the error
///     travels the way every other refusal does.
/// </remarks>
public class ACurrencyMismatchAnswers422Tests
{
    [Fact]
    public void AddingEuroAndDollars_Answers422_NamingBothCurrencies()
    {
        var sum = Money.From(10m, CurrencyCode.EUR).Add(Money.From(5m, CurrencyCode.USD));

        var problem = sum.ToHttpResult().Should().BeOfType<ProblemHttpResult>().Subject;

        problem.StatusCode.Should().Be(422);
        problem.ProblemDetails.Extensions["code"].Should().Be("CURRENCY_MISMATCH");
        problem.ProblemDetails.Extensions["left"].Should().Be("EUR");
        problem.ProblemDetails.Extensions["right"].Should().Be("USD");
    }

    /// <summary>The control: one currency answers the sum.</summary>
    [Fact]
    public void AddingTwoEuroAmounts_AnswersTheSum()
    {
        var sum = Money.From(10m, CurrencyCode.EUR).Add(Money.From(5m, CurrencyCode.EUR));

        sum.ToHttpResult().Should().BeOfType<Ok<Money>>().Subject.Value
            .Should().Be(Money.From(15m, CurrencyCode.EUR));
    }
}
