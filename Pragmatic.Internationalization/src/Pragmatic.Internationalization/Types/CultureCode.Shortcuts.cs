namespace Pragmatic.Internationalization.Types;

/// <summary>
///     Convenience static shortcuts for common <see cref="CultureCode"/> values
///     (language-only and language+country).
/// </summary>
public readonly partial struct CultureCode
{
    #region Static Shortcuts - Language Only

    /// <summary>Gets the English language culture (no country).</summary>
    public static CultureCode English => new(LanguageCode.English);

    /// <summary>Gets the Italian language culture (no country).</summary>
    public static CultureCode Italian => new(LanguageCode.Italian);

    /// <summary>Gets the German language culture (no country).</summary>
    public static CultureCode German => new(LanguageCode.German);

    /// <summary>Gets the French language culture (no country).</summary>
    public static CultureCode French => new(LanguageCode.French);

    /// <summary>Gets the Spanish language culture (no country).</summary>
    public static CultureCode Spanish => new(LanguageCode.Spanish);

    /// <summary>Gets the Portuguese language culture (no country).</summary>
    public static CultureCode Portuguese => new(LanguageCode.Portuguese);

    /// <summary>Gets the Chinese language culture (no country).</summary>
    public static CultureCode Chinese => new(LanguageCode.Chinese);

    /// <summary>Gets the Japanese language culture (no country).</summary>
    public static CultureCode Japanese => new(LanguageCode.Japanese);

    /// <summary>Gets the Arabic language culture (no country).</summary>
    public static CultureCode Arabic => new(LanguageCode.Arabic);

    #endregion

    #region Static Shortcuts - Language + Country

    /// <summary>Gets the English (United States) culture.</summary>
    public static CultureCode EnglishUS => new(LanguageCode.English, CountryCode.US);

    /// <summary>Gets the English (United Kingdom) culture.</summary>
    public static CultureCode EnglishUK => new(LanguageCode.English, CountryCode.UK);

    /// <summary>Gets the English (Australia) culture.</summary>
    public static CultureCode EnglishAustralia => new(LanguageCode.English, CountryCode.Australia);

    /// <summary>Gets the German (Germany) culture.</summary>
    public static CultureCode GermanGermany => new(LanguageCode.German, CountryCode.Germany);

    /// <summary>Gets the German (Switzerland) culture.</summary>
    public static CultureCode GermanSwitzerland => new(LanguageCode.German, CountryCode.Switzerland);

    /// <summary>Gets the German (Austria) culture.</summary>
    public static CultureCode GermanAustria => new(LanguageCode.German, CountryCode.Austria);

    /// <summary>Gets the French (France) culture.</summary>
    public static CultureCode FrenchFrance => new(LanguageCode.French, CountryCode.France);

    /// <summary>Gets the French (Canada) culture.</summary>
    public static CultureCode FrenchCanada => new(LanguageCode.French, CountryCode.Canada);

    /// <summary>Gets the French (Switzerland) culture.</summary>
    public static CultureCode FrenchSwitzerland => new(LanguageCode.French, CountryCode.Switzerland);

    /// <summary>Gets the Spanish (Spain) culture.</summary>
    public static CultureCode SpanishSpain => new(LanguageCode.Spanish, CountryCode.Spain);

    /// <summary>Gets the Spanish (Mexico) culture.</summary>
    public static CultureCode SpanishMexico => new(LanguageCode.Spanish, CountryCode.Mexico);

    /// <summary>Gets the Portuguese (Brazil) culture.</summary>
    public static CultureCode PortugueseBrazil => new(LanguageCode.Portuguese, CountryCode.Brazil);

    /// <summary>Gets the Portuguese (Portugal) culture.</summary>
    public static CultureCode PortuguesePortugal => new(LanguageCode.Portuguese, CountryCode.Portugal);

    /// <summary>Gets the Chinese (China) culture.</summary>
    public static CultureCode ChineseChina => new(LanguageCode.Chinese, CountryCode.China);

    /// <summary>Gets the Chinese (Taiwan) culture.</summary>
    public static CultureCode ChineseTaiwan => new(LanguageCode.Chinese, CountryCode.Taiwan);

    /// <summary>Gets the Japanese (Japan) culture.</summary>
    public static CultureCode JapaneseJapan => new(LanguageCode.Japanese, CountryCode.Japan);

    #endregion
}
