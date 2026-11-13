using Pragmatic.Internationalization.Types;

namespace Pragmatic.Result.Monetary;

/// <summary>
///     Arithmetic on <see cref="Money" /> that answers a currency mismatch with <see cref="CurrencyMismatchError" />
///     instead of throwing.
/// </summary>
/// <remarks>
///     <para>
///         <c>money + other</c> throws when the currencies differ, which is right where the code knows they agree,
///         and wrong where they come from a request: the exception reaches the host as a 500. These are the path
///         for amounts from input, and the failure travels like any other refusal: a 422 with both currencies.
///     </para>
///     <para>
///         Extension members rather than members of <see cref="Money" /> because <c>Money</c> lives in
///         <c>Pragmatic.Internationalization</c>, which <c>Pragmatic.Result</c> references: the type cannot name
///         <c>Result</c> without a cycle. Called the same way: <c>price.Add(tax)</c>.
///     </para>
/// </remarks>
public static class MoneyResultExtensions
{
    extension(Money money)
    {
        /// <summary>The sum, or <see cref="CurrencyMismatchError" /> when the currencies differ.</summary>
        public Result<Money, CurrencyMismatchError> Add(Money other)
            => money.Currency == other.Currency ? money + other : Mismatch(money, other);

        /// <summary>The difference, or <see cref="CurrencyMismatchError" /> when the currencies differ.</summary>
        public Result<Money, CurrencyMismatchError> Subtract(Money other)
            => money.Currency == other.Currency ? money - other : Mismatch(money, other);

        /// <summary>
        ///     Less than zero, zero or greater than zero as <see cref="Money.CompareTo" /> answers, or
        ///     <see cref="CurrencyMismatchError" /> when the currencies differ.
        /// </summary>
        public Result<int, CurrencyMismatchError> Compare(Money other)
            => money.Currency == other.Currency ? money.CompareTo(other) : Mismatch(money, other);
    }

    private static CurrencyMismatchError Mismatch(Money left, Money right)
        => new() { Left = left.Currency.Code, Right = right.Currency.Code };
}
