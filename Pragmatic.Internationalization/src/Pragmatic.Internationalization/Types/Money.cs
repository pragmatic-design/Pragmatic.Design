using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using Pragmatic.Internationalization.Context;

namespace Pragmatic.Internationalization.Types;

/// <summary>
///     Represents a monetary value with its associated currency.
/// </summary>
/// <remarks>
///     <para>
///         Money is an immutable value type that combines an amount with a currency code.
///         This ensures that monetary values always have context and prevents accidental
///         mixing of different currencies.
///     </para>
///     <para>
///         Arithmetic operations are only allowed between Money instances of the same currency.
///         Attempting to add or subtract Money with different currencies throws <see cref="InvalidOperationException" />.
///     </para>
///     <para>
///         For currency conversion, use a dedicated exchange service - Money does not support implicit conversion.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// var price = Money.From(99.99m, CurrencyCode.USD);
/// var tax = Money.From(7.50m, CurrencyCode.USD);
/// var total = price + tax; // OK - same currency
/// 
/// var euroPrice = Money.From(85.00m, CurrencyCode.EUR);
/// // var mixed = price + euroPrice; // Throws InvalidOperationException!
/// </code>
/// </example>
public readonly partial struct Money : IEquatable<Money>, IComparable<Money>
{
    /// <summary>
    ///     Gets the monetary amount.
    /// </summary>
    public decimal Amount { get; }

    /// <summary>
    ///     Gets the currency code.
    /// </summary>
    public CurrencyCode Currency { get; }

    private Money(decimal amount, CurrencyCode currency)
    {
        Amount = amount;
        Currency = currency;
    }

    // =============================================================================
    // Factory Methods
    // =============================================================================

    /// <summary>
    ///     Creates a new Money instance from an amount and currency code.
    /// </summary>
    /// <param name="amount">The monetary amount.</param>
    /// <param name="currency">The currency code.</param>
    /// <returns>A new Money instance.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Money From(decimal amount, CurrencyCode currency)
    {
        return new Money(amount, currency);
    }

    /// <summary>
    ///     Creates a new Money instance from an amount and currency code string.
    /// </summary>
    /// <param name="amount">The monetary amount.</param>
    /// <param name="currencyCode">The ISO 4217 currency code string.</param>
    /// <returns>A new Money instance.</returns>
    /// <exception cref="ArgumentException">Thrown if the currency code is invalid.</exception>
    public static Money From(decimal amount, string currencyCode)
    {
        return new Money(amount, CurrencyCode.FromCode(currencyCode));
    }

    /// <summary>
    ///     Creates a Money instance representing zero in the specified currency.
    /// </summary>
    /// <param name="currency">The currency code.</param>
    /// <returns>A Money instance with zero amount.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Money Zero(CurrencyCode currency)
    {
        return new Money(0m, currency);
    }

    /// <summary>
    ///     Attempts to create a Money instance from an amount and currency code string.
    /// </summary>
    /// <param name="amount">The monetary amount.</param>
    /// <param name="currencyCode">The ISO 4217 currency code string.</param>
    /// <param name="money">When successful, contains the Money instance; otherwise, default.</param>
    /// <returns>True if the currency code is valid; otherwise, false.</returns>
    public static bool TryFrom(decimal amount, string? currencyCode, out Money money)
    {
        if (CurrencyCode.TryFromCode(currencyCode, out var currency))
        {
            money = new Money(amount, currency);
            return true;
        }

        money = default;
        return false;
    }

    // =============================================================================
    // Helper Properties
    // =============================================================================

    /// <summary>
    ///     Gets whether this Money represents a zero amount.
    /// </summary>
    public bool IsZero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Amount == 0m;
    }

    /// <summary>
    ///     Gets whether this Money represents a positive amount (greater than zero).
    /// </summary>
    public bool IsPositive
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Amount > 0m;
    }

    /// <summary>
    ///     Gets whether this Money represents a negative amount (less than zero).
    /// </summary>
    public bool IsNegative
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Amount < 0m;
    }

    /// <summary>
    ///     Returns the absolute value of this Money.
    /// </summary>
    /// <returns>A Money instance with the absolute amount.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Money Abs()
    {
        return new Money(Math.Abs(Amount), Currency);
    }

    // =============================================================================
    // Rounding
    // =============================================================================

    /// <summary>
    ///     Rounds the amount to the specified number of decimal places.
    /// </summary>
    /// <param name="decimals">The number of decimal places. If null, uses the currency's MinorUnits.</param>
    /// <param name="mode">
    ///     The rounding mode to use. Defaults to <see cref="MidpointRounding.ToEven" /> (banker's rounding);
    ///     callers wanting half-up behaviour must pass <see cref="MidpointRounding.AwayFromZero" /> explicitly.
    /// </param>
    /// <returns>A new Money with the rounded amount.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Money Round(int? decimals = null, MidpointRounding mode = MidpointRounding.ToEven)
    {
        var places = decimals ?? Currency.MinorUnits;
        return new Money(Math.Round(Amount, places, mode), Currency);
    }

    /// <summary>
    ///     Rounds the amount to the currency's standard minor units (e.g., 2 for USD, 0 for JPY).
    /// </summary>
    /// <param name="mode">
    ///     The rounding mode to use. Defaults to <see cref="MidpointRounding.ToEven" /> (banker's rounding);
    ///     callers wanting half-up behaviour must pass <see cref="MidpointRounding.AwayFromZero" /> explicitly.
    /// </param>
    /// <returns>A new Money with the rounded amount.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Money RoundToMinorUnit(MidpointRounding mode = MidpointRounding.ToEven)
    {
        return Round(Currency.MinorUnits, mode);
    }

    // =============================================================================
    // Formatting
    // =============================================================================

    // Cache NumberFormatInfo per (CultureName, CurrencySymbol, MinorUnits) to avoid cloning on every Format call
    private static readonly ConcurrentDictionary<(string CultureName, string Symbol, int MinorUnits), NumberFormatInfo>
        SNfiCache = new();

    /// <summary>
    ///     Formats the Money using the current culture or <see cref="I18NContext.Current" />.
    /// </summary>
    /// <returns>A formatted string representation of the monetary value.</returns>
    public string Format()
    {
        return Format(I18NContext.EffectiveCulture);
    }

    /// <summary>
    ///     Formats the Money using the specified culture.
    /// </summary>
    /// <param name="culture">The culture to use for formatting.</param>
    /// <returns>A formatted string representation of the monetary value.</returns>
    public string Format(CultureInfo culture)
    {
        Ensure.Ensure.ThrowIfNull(culture);

        var key = (culture.Name, Currency.Symbol, Currency.MinorUnits);
        var nfi = SNfiCache.GetOrAdd(key, static (k, baseCulture) =>
        {
            var info = (NumberFormatInfo)baseCulture.NumberFormat.Clone();
            info.CurrencySymbol = k.Symbol;
            info.CurrencyDecimalDigits = k.MinorUnits;
            return info;
        }, culture);

        return Amount.ToString("C", nfi);
    }

    // =============================================================================
    // Equality and Comparison
    // =============================================================================

    /// <inheritdoc />
    public bool Equals(Money other)
    {
        return Amount == other.Amount && Currency == other.Currency;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Money other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Amount, Currency);
    }

    /// <summary>
    ///     Compares this Money to another Money of the same currency.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if currencies don't match.</exception>
    public int CompareTo(Money other)
    {
        EnsureSameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    // =============================================================================
    // String Representation
    // =============================================================================

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Amount.ToString(CultureInfo.InvariantCulture)} {Currency.Code}";
    }

    // =============================================================================
    // Deconstruction
    // =============================================================================

    /// <summary>
    ///     Deconstructs the Money into its components.
    /// </summary>
    public void Deconstruct(out decimal amount, out CurrencyCode currency)
    {
        amount = Amount;
        currency = Currency;
    }

    // =============================================================================
    // Helpers
    // =============================================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            ThrowCurrencyMismatch(left.Currency, right.Currency);
    }

    [DoesNotReturn]
    private static void ThrowCurrencyMismatch(CurrencyCode left, CurrencyCode right)
    {
        throw new InvalidOperationException(
            $"Cannot perform operation on Money with different currencies: {left.Code} and {right.Code}. " +
            "Use explicit currency conversion.");
    }
}