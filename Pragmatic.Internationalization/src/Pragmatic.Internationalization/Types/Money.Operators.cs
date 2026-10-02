using System.Runtime.CompilerServices;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Internationalization.Types;

public readonly partial struct Money
{
    // =============================================================================
    // Arithmetic Operators
    // =============================================================================

    /// <summary>
    ///     Adds two Money instances of the same currency.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if currencies don't match.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    /// <summary>
    ///     Subtracts two Money instances of the same currency.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if currencies don't match.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    /// <summary>
    ///     Multiplies a Money by a decimal factor.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Money operator *(Money money, decimal factor)
    {
        return new Money(money.Amount * factor, money.Currency);
    }

    /// <summary>
    ///     Multiplies a Money by a decimal factor.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Money operator *(decimal factor, Money money)
    {
        return new Money(money.Amount * factor, money.Currency);
    }

    /// <summary>
    ///     Divides a Money by a decimal divisor.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if divisor is zero.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Money operator /(Money money, decimal divisor)
    {
        ThrowIfZero(divisor);
        return new Money(money.Amount / divisor, money.Currency);
    }

    /// <summary>
    ///     Negates a Money amount.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Money operator -(Money money)
    {
        return new Money(-money.Amount, money.Currency);
    }

    /// <summary>
    ///     Returns the Money unchanged (unary plus).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Money operator +(Money money)
    {
        return money;
    }

    // =============================================================================
    // Comparison Operators
    // =============================================================================

    /// <summary>
    ///     Compares two Money instances of the same currency.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if currencies don't match.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount > right.Amount;
    }

    /// <summary>
    ///     Compares two Money instances of the same currency.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if currencies don't match.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount < right.Amount;
    }

    /// <summary>
    ///     Compares two Money instances of the same currency.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if currencies don't match.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >=(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount >= right.Amount;
    }

    /// <summary>
    ///     Compares two Money instances of the same currency.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if currencies don't match.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <=(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount <= right.Amount;
    }

    /// <summary>
    ///     Determines whether two Money instances are equal.
    /// </summary>
    public static bool operator ==(Money left, Money right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two Money instances are not equal.
    /// </summary>
    public static bool operator !=(Money left, Money right)
    {
        return !left.Equals(right);
    }
}