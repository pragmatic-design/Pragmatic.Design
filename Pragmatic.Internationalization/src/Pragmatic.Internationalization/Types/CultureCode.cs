using System.ComponentModel;
using System.Globalization;

namespace Pragmatic.Internationalization.Types;

/// <summary>
///     Represents a culture code combining a language and an optional country.
/// </summary>
/// <remarks>
///     <para>
///         CultureCode can represent either a language-only culture (e.g., "en", "it") or
///         a language with country specialization (e.g., "en-US", "de-CH").
///     </para>
///     <para>
///         Use language-only for simple applications where regional differences don't matter.
///         Use language+country for e-commerce, finance, or multi-regional applications.
///     </para>
/// </remarks>
[TypeConverter(typeof(CultureCodeTypeConverter))]
public readonly partial struct CultureCode : IEquatable<CultureCode>
{
    /// <summary>
    ///     Gets the language component of this culture.
    /// </summary>
    public LanguageCode Language { get; }

    /// <summary>
    ///     Gets the optional country component of this culture.
    /// </summary>
    /// <remarks>
    ///     Null for language-only cultures (e.g., "en", "it").
    ///     Has value for regional cultures (e.g., "en-US", "de-CH").
    /// </remarks>
    public CountryCode? Country { get; }

    /// <summary>
    ///     Gets the combined culture code string (e.g., "en-US" or "en").
    /// </summary>
    public string Code => Country.HasValue
        ? $"{Language.Code}-{Country.Value.Code}"
        : Language.Code;

    /// <summary>
    ///     Gets the default currency for this culture.
    /// </summary>
    /// <remarks>
    ///     If a country is specified, uses the country's default currency.
    ///     Otherwise, falls back to a default currency for the language.
    /// </remarks>
    public CurrencyCode DefaultCurrency => Country?.DefaultCurrency
        ?? GetDefaultCurrencyForLanguage(Language);

    /// <summary>
    ///     Gets a value indicating whether this culture's language is written right-to-left.
    /// </summary>
    public bool IsRightToLeft => Language.IsRightToLeft;

    /// <summary>
    ///     Gets the plural rule family for this culture's language.
    /// </summary>
    public PluralRuleFamily PluralFamily => Language.PluralFamily;

    /// <summary>
    ///     Creates a language-only culture code.
    /// </summary>
    /// <param name="language">The language.</param>
    public CultureCode(LanguageCode language)
    {
        Language = language;
        Country = null;
    }

    /// <summary>
    ///     Creates a culture code with language and country.
    /// </summary>
    /// <param name="language">The language.</param>
    /// <param name="country">The country/region.</param>
    public CultureCode(LanguageCode language, CountryCode country)
    {
        Language = language;
        Country = country;
    }

    #region Conversion

    /// <summary>
    ///     Converts this CultureCode to a .NET CultureInfo.
    /// </summary>
    /// <returns>The corresponding CultureInfo.</returns>
    public CultureInfo ToCultureInfo()
    {
        return CultureInfo.GetCultureInfo(Code);
    }

    #endregion

    #region Equality

    /// <inheritdoc />
    public bool Equals(CultureCode other)
    {
        return Language.Equals(other.Language) &&
               Nullable.Equals(Country, other.Country);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is CultureCode other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Language, Country);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Code;
    }

    /// <summary>
    ///     Determines whether two CultureCode instances are equal.
    /// </summary>
    public static bool operator ==(CultureCode left, CultureCode right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two CultureCode instances are not equal.
    /// </summary>
    public static bool operator !=(CultureCode left, CultureCode right)
    {
        return !left.Equals(right);
    }

    #endregion

    #region Implicit Conversions

    /// <summary>
    ///     Implicitly converts a CultureCode to its string representation.
    /// </summary>
    public static implicit operator string(CultureCode culture)
    {
        return culture.Code;
    }

    /// <summary>
    ///     Implicitly converts a string to a CultureCode.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if the code is not valid.</exception>
    public static implicit operator CultureCode(string code)
    {
        return FromString(code);
    }

    /// <summary>
    ///     Implicitly converts a CultureCode to a CultureInfo.
    /// </summary>
    public static implicit operator CultureInfo(CultureCode culture)
    {
        return culture.ToCultureInfo();
    }

    #endregion

    #region Private Helpers

    private static CurrencyCode GetDefaultCurrencyForLanguage(LanguageCode language)
    {
        // Map languages to their most common currency
        // This is a fallback when no country is specified
        return language.Code switch
        {
            "en" => CurrencyCode.USD,
            "it" => CurrencyCode.EUR,
            "de" => CurrencyCode.EUR,
            "fr" => CurrencyCode.EUR,
            "es" => CurrencyCode.EUR,
            "pt" => CurrencyCode.EUR,
            "ja" => CurrencyCode.JPY,
            "zh" => CurrencyCode.CNY,
            "ko" => CurrencyCode.KRW,
            "ru" => CurrencyCode.RUB,
            "ar" => CurrencyCode.SAR,
            "hi" => CurrencyCode.INR,
            "tr" => CurrencyCode.TRY,
            "pl" => CurrencyCode.PLN,
            "nl" => CurrencyCode.EUR,
            "sv" => CurrencyCode.SEK,
            "da" => CurrencyCode.DKK,
            "no" or "nb" or "nn" => CurrencyCode.NOK,
            "fi" => CurrencyCode.EUR,
            "cs" => CurrencyCode.CZK,
            "hu" => CurrencyCode.HUF,
            "ro" => CurrencyCode.RON,
            "th" => CurrencyCode.THB,
            "vi" => CurrencyCode.VND,
            "id" => CurrencyCode.IDR,
            "ms" => CurrencyCode.MYR,
            "he" or "iw" => CurrencyCode.ILS,
            _ => CurrencyCode.USD // Fallback to USD
        };
    }

    #endregion
}
