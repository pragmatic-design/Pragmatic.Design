using System.Diagnostics.CodeAnalysis;

namespace Pragmatic.Internationalization.Types;

/// <summary>
///     Represents an ISO 4217 currency code with associated metadata.
/// </summary>
/// <remarks>
///     <para>
///         This is an immutable value type representing a currency. Currency codes are source-generated
///         from ISO 4217 data, providing IntelliSense support for all standard currencies.
///     </para>
///     <para>
///         Use the static properties (e.g., CurrencyCode.USD, CurrencyCode.EUR) for compile-time
///         currency references, or <see cref="FromCode" /> for runtime parsing.
///     </para>
/// </remarks>
public readonly partial struct CurrencyCode : IEquatable<CurrencyCode>
{
    /// <summary>
    ///     Gets the ISO 4217 three-letter currency code (e.g., "USD", "EUR", "JPY").
    /// </summary>
    public string Code { get; }

    /// <summary>
    ///     Gets the full name of the currency (e.g., "US Dollar", "Euro").
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the currency symbol (e.g., "$", "€", "¥").
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    ///     Gets the number of decimal places used for minor units (e.g., 2 for USD, 0 for JPY).
    /// </summary>
    public int MinorUnits { get; }

    /// <summary>
    ///     Creates a new currency code instance.
    /// </summary>
    /// <remarks>
    ///     This constructor is internal. Use the static properties or factory methods instead.
    /// </remarks>
    internal CurrencyCode(string code, string name, string symbol, int minorUnits)
    {
        Code = code;
        Name = name;
        Symbol = symbol;
        MinorUnits = minorUnits;
    }

    /// <summary>
    ///     Creates a CurrencyCode from a string code.
    /// </summary>
    /// <param name="code">The ISO 4217 three-letter currency code.</param>
    /// <returns>The corresponding CurrencyCode.</returns>
    /// <exception cref="ArgumentException">Thrown if the code is not a valid ISO 4217 currency code.</exception>
    public static CurrencyCode FromCode(string code)
    {
        if (!TryFromCode(code, out var currency))
            throw new ArgumentException($"'{code}' is not a valid ISO 4217 currency code.", nameof(code));
        return currency;
    }

    /// <summary>
    ///     Attempts to create a CurrencyCode from a string code.
    /// </summary>
    /// <param name="code">The ISO 4217 three-letter currency code.</param>
    /// <param name="currency">When successful, contains the CurrencyCode; otherwise, default.</param>
    /// <returns>True if the code is valid; otherwise, false.</returns>
    public static bool TryFromCode(string? code, out CurrencyCode currency)
    {
        if (string.IsNullOrEmpty(code))
        {
            currency = default;
            return false;
        }

        // Generated code will provide the lookup
        return TryFromCodeInternal(code.ToUpperInvariant(), out currency);
    }

    /// <summary>
    ///     Checks if a string is a valid ISO 4217 currency code.
    /// </summary>
    /// <param name="code">The code to check.</param>
    /// <returns>True if valid; otherwise, false.</returns>
    public static bool IsValid([NotNullWhen(true)] string? code)
    {
        return TryFromCode(code, out _);
    }

    // This will be implemented by the source generator
    private static partial void TryFromCodeInternal(string code, out CurrencyCode currency, out bool found);

    private static bool TryFromCodeInternal(string code, out CurrencyCode currency)
    {
        TryFromCodeInternal(code, out currency, out var found);
        return found;
    }

    /// <inheritdoc />
    public bool Equals(CurrencyCode other)
    {
        return string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is CurrencyCode other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(Code ?? string.Empty);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Code ?? string.Empty;
    }

    /// <summary>
    ///     Determines whether two CurrencyCode instances are equal.
    /// </summary>
    public static bool operator ==(CurrencyCode left, CurrencyCode right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two CurrencyCode instances are not equal.
    /// </summary>
    public static bool operator !=(CurrencyCode left, CurrencyCode right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     Implicitly converts a CurrencyCode to its string representation.
    /// </summary>
    public static implicit operator string(CurrencyCode currency)
    {
        return currency.Code;
    }
}