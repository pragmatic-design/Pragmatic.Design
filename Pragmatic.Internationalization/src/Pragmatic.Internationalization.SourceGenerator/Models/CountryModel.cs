// =============================================================================
// Pragmatic.Internationalization - CountryModel
// Immutable model for country code generation caching
// =============================================================================

using System.Collections.Immutable;

namespace Pragmatic.Internationalization.SourceGenerator.Models;

/// <summary>
///     Represents a single ISO 3166-1 country for source generation.
/// </summary>
internal readonly record struct CountryModel(
    string Code,
    string Name,
    string NativeName,
    string DefaultLanguage,
    string DefaultCurrency,
    string DateFormat,
    char DecimalSeparator,
    char GroupSeparator)
{
    /// <summary>
    ///     Gets the escaped name for use in C# string literals.
    /// </summary>
    public string EscapedName => EscapeString(Name);

    /// <summary>
    ///     Gets the escaped native name for use in C# string literals.
    /// </summary>
    public string EscapedNativeName => EscapeString(NativeName);

    /// <summary>
    ///     Gets the property name for this country.
    /// </summary>
    /// <remarks>
    ///     Uses short forms where conventional (US, UK, UAE) and full names otherwise.
    /// </remarks>
    public string PropertyName => ToPropertyName(Code, Name);

    private static string EscapeString(string value) => value
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"");

    private static string ToPropertyName(string code, string name)
    {
        // Short conventional names by code
        return code switch
        {
            "US" => "US",
            "GB" => "UK",  // UK is more commonly used than GB
            "AE" => "UAE", // United Arab Emirates
            "CZ" => "CzechRepublic",
            "KR" => "SouthKorea",
            "HK" => "HongKong",
            "NZ" => "NewZealand",
            "ZA" => "SouthAfrica",
            "SA" => "SaudiArabia",
            _ => name.Replace(" ", "").Replace("-", "")
        };
    }

    /// <summary>
    ///     Gets the escaped group separator for C# char literal.
    /// </summary>
    public string EscapedGroupSeparator => GroupSeparator switch
    {
        '\'' => "\\'",
        '\\' => "\\\\",
        _ => GroupSeparator.ToString()
    };
}

/// <summary>
///     Aggregated model containing all countries for generation.
/// </summary>
internal readonly record struct CountryCodeGenerationModel(
    ImmutableArray<CountryModel> Countries)
{
    /// <summary>
    ///     Gets whether this model has valid countries to generate.
    /// </summary>
    public bool IsValid => !Countries.IsDefaultOrEmpty;
}
