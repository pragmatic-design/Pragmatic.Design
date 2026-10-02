using System.Diagnostics.CodeAnalysis;

namespace Pragmatic.Internationalization.Types;

/// <summary>
///     Represents an ISO 3166-1 alpha-2 country code with associated metadata.
/// </summary>
/// <remarks>
///     <para>
///         This is an immutable value type representing a country/region. Country codes are source-generated
///         from ISO 3166-1 data, providing IntelliSense support for all standard countries.
///     </para>
///     <para>
///         Use the static properties (e.g., CountryCode.US, CountryCode.Italy) for compile-time
///         country references, or <see cref="FromCode" /> for runtime parsing.
///     </para>
///     <para>
///         Note: We use "Country" instead of "Region" to avoid ambiguity with sub-national regions
///         (e.g., Trentino-Alto Adige, California).
///     </para>
/// </remarks>
public readonly partial struct CountryCode : IEquatable<CountryCode>
{
    /// <summary>
    ///     Gets the ISO 3166-1 alpha-2 two-letter country code (e.g., "US", "IT", "DE").
    /// </summary>
    public string Code { get; }

    /// <summary>
    ///     Gets the English name of the country (e.g., "United States", "Italy", "Germany").
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the native name of the country (e.g., "United States", "Italia", "Deutschland").
    /// </summary>
    public string NativeName { get; }

    /// <summary>
    ///     Gets the default/official language of this country.
    /// </summary>
    /// <remarks>
    ///     For countries with multiple official languages, this returns the most widely used one.
    ///     For example, Switzerland returns German, Belgium returns Dutch.
    /// </remarks>
    public LanguageCode DefaultLanguage { get; }

    /// <summary>
    ///     Gets the default currency used in this country.
    /// </summary>
    public CurrencyCode DefaultCurrency { get; }

    /// <summary>
    ///     Gets the standard date format pattern used in this country (e.g., "M/d/yyyy", "dd/MM/yyyy").
    /// </summary>
    public string DateFormat { get; }

    /// <summary>
    ///     Gets the decimal separator used for numbers in this country (e.g., '.', ',').
    /// </summary>
    public char NumberDecimalSeparator { get; }

    /// <summary>
    ///     Gets the group separator used for numbers in this country (e.g., ',', '.', ' ').
    /// </summary>
    public char NumberGroupSeparator { get; }

    /// <summary>
    ///     Creates a new country code instance.
    /// </summary>
    /// <remarks>
    ///     This constructor is internal. Use the static properties or factory methods instead.
    /// </remarks>
    internal CountryCode(
        string code,
        string name,
        string nativeName,
        LanguageCode defaultLanguage,
        CurrencyCode defaultCurrency,
        string dateFormat,
        char numberDecimalSeparator,
        char numberGroupSeparator)
    {
        Code = code;
        Name = name;
        NativeName = nativeName;
        DefaultLanguage = defaultLanguage;
        DefaultCurrency = defaultCurrency;
        DateFormat = dateFormat;
        NumberDecimalSeparator = numberDecimalSeparator;
        NumberGroupSeparator = numberGroupSeparator;
    }

    /// <summary>
    ///     Creates a CountryCode from a string code.
    /// </summary>
    /// <param name="code">The ISO 3166-1 alpha-2 two-letter country code.</param>
    /// <returns>The corresponding CountryCode.</returns>
    /// <exception cref="ArgumentException">Thrown if the code is not a valid ISO 3166-1 country code.</exception>
    public static CountryCode FromCode(string code)
    {
        if (!TryFromCode(code, out var country))
            throw new ArgumentException($"'{code}' is not a valid ISO 3166-1 country code.", nameof(code));
        return country;
    }

    /// <summary>
    ///     Attempts to create a CountryCode from a string code.
    /// </summary>
    /// <param name="code">The ISO 3166-1 alpha-2 two-letter country code.</param>
    /// <param name="country">When successful, contains the CountryCode; otherwise, default.</param>
    /// <returns>True if the code is valid; otherwise, false.</returns>
    public static bool TryFromCode(string? code, out CountryCode country)
    {
        if (string.IsNullOrEmpty(code))
        {
            country = default;
            return false;
        }

        // Generated code will provide the lookup
        return TryFromCodeInternal(code.ToUpperInvariant(), out country);
    }

    /// <summary>
    ///     Checks if a string is a valid ISO 3166-1 country code.
    /// </summary>
    /// <param name="code">The code to check.</param>
    /// <returns>True if valid; otherwise, false.</returns>
    public static bool IsValid([NotNullWhen(true)] string? code)
    {
        return TryFromCode(code, out _);
    }

    // This will be implemented by the source generator
    private static partial void TryFromCodeInternal(string code, out CountryCode country, out bool found);

    private static bool TryFromCodeInternal(string code, out CountryCode country)
    {
        TryFromCodeInternal(code, out country, out var found);
        return found;
    }

    /// <inheritdoc />
    public bool Equals(CountryCode other)
    {
        return string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is CountryCode other && Equals(other);
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
    ///     Determines whether two CountryCode instances are equal.
    /// </summary>
    public static bool operator ==(CountryCode left, CountryCode right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two CountryCode instances are not equal.
    /// </summary>
    public static bool operator !=(CountryCode left, CountryCode right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     Implicitly converts a CountryCode to its string representation.
    /// </summary>
    public static implicit operator string(CountryCode country)
    {
        return country.Code;
    }

    /// <summary>
    ///     Implicitly converts a string to a CountryCode.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if the code is not valid.</exception>
    public static implicit operator CountryCode(string code)
    {
        return FromCode(code);
    }
}
