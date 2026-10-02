namespace Pragmatic.Internationalization.Types;

/// <summary>
///     Represents the plural rule family for a language.
/// </summary>
/// <remarks>
///     <para>
///         Different languages have different plural rules. This enum groups languages
///         by their plural rule behavior, based on Unicode CLDR.
///     </para>
///     <para>
///         See: https://cldr.unicode.org/index/cldr-spec/plural-rules
///     </para>
/// </remarks>
public enum PluralRuleFamily
{
    /// <summary>
    ///     Germanic languages: English, German, Dutch, Swedish, Danish, Norwegian, etc.
    ///     Rules: One (1), Other (everything else).
    /// </summary>
    /// <example>1 item, 2 items, 0 items</example>
    Germanic,

    /// <summary>
    ///     Romance languages: French, Italian, Spanish, Portuguese, Catalan, etc.
    ///     Rules: One (0-1), Other (2+).
    ///     Note: Some Romance languages treat 0 as singular.
    /// </summary>
    /// <example>0 élément, 1 élément, 2 éléments (French)</example>
    Romance,

    /// <summary>
    ///     East Slavic languages: Russian, Ukrainian, Belarusian.
    ///     Rules: One (1, 21, 31...), Few (2-4, 22-24...), Many (0, 5-20, 25-30...).
    /// </summary>
    /// <example>1 файл, 2 файла, 5 файлов (Russian)</example>
    Slavic,

    /// <summary>
    ///     West Slavic languages: Polish, Czech, Slovak.
    ///     Rules: One (1), Few (2-4), Many (0, 5+).
    /// </summary>
    Polish,

    /// <summary>
    ///     Celtic languages: Welsh, Irish, Scottish Gaelic.
    ///     Rules: Zero, One, Two, Few, Many, Other.
    /// </summary>
    Celtic,

    /// <summary>
    ///     Arabic language.
    ///     Rules: Zero (0), One (1), Two (2), Few (3-10), Many (11-99), Other (100+).
    /// </summary>
    Arabic,

    /// <summary>
    ///     Baltic languages: Latvian, Lithuanian.
    ///     Special rules based on number endings.
    /// </summary>
    Baltic,

    /// <summary>
    ///     East Asian languages: Chinese, Japanese, Korean, Vietnamese, Thai, etc.
    ///     Rules: Other only (no plural forms).
    /// </summary>
    /// <example>1個, 2個, 100個 (same form)</example>
    Asian,

    /// <summary>
    ///     Semitic languages other than Arabic: Hebrew.
    ///     Rules vary by language.
    /// </summary>
    Semitic,

    /// <summary>
    ///     Indic languages: Hindi, Bengali, Gujarati, etc.
    ///     Rules: One (0-1), Other (2+).
    /// </summary>
    Indic,

    /// <summary>
    ///     Languages with no plural distinctions or unknown rules.
    ///     Falls back to Germanic (One/Other) behavior.
    /// </summary>
    Other
}
