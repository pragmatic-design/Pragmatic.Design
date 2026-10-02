// =============================================================================
// Pragmatic.Internationalization - CountryParser
// Simple JSON parser for ISO 3166-1 country codes (netstandard2.0 compatible)
// =============================================================================

using System.Collections.Immutable;
using System.Text;

using Pragmatic.Internationalization.SourceGenerator.Models;

namespace Pragmatic.Internationalization.SourceGenerator;

/// <summary>
///     Parses country JSON data into models for source generation.
/// </summary>
internal static class CountryParser
{
    /// <summary>
    ///     Parses JSON content into country models.
    /// </summary>
    /// <remarks>
    ///     Simple JSON parsing without System.Text.Json (not available in netstandard2.0).
    /// </remarks>
    public static ImmutableArray<CountryModel> Parse(string json)
    {
        var countries = ImmutableArray.CreateBuilder<CountryModel>();

        var currentProperty = "";
        var currentValue = new StringBuilder();
        var inString = false;
        var escape = false;

        string? code = null;
        string? name = null;
        string? nativeName = null;
        string? defaultLanguage = null;
        string? defaultCurrency = null;
        string? dateFormat = null;
        char? decimalSeparator = null;
        char? groupSeparator = null;

        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];

            if (escape)
            {
                if (inString)
                    currentValue.Append(c);
                escape = false;
                continue;
            }

            if (c == '\\')
            {
                escape = true;
                if (inString)
                    currentValue.Append(c);
                continue;
            }

            if (c == '"')
            {
                if (inString)
                {
                    inString = false;
                    var value = currentValue.ToString();
                    currentValue.Clear();

                    if (string.IsNullOrEmpty(currentProperty))
                    {
                        currentProperty = value;
                    }
                    else
                    {
                        switch (currentProperty.ToLowerInvariant())
                        {
                            case "code":
                                code = value;
                                break;
                            case "name":
                                name = value;
                                break;
                            case "nativename":
                                nativeName = value;
                                break;
                            case "defaultlanguage":
                                defaultLanguage = value;
                                break;
                            case "defaultcurrency":
                                defaultCurrency = value;
                                break;
                            case "dateformat":
                                dateFormat = value;
                                break;
                            case "decimalseparator":
                                decimalSeparator = value.Length > 0 ? value[0] : '.';
                                break;
                            case "groupseparator":
                                groupSeparator = value.Length > 0 ? value[0] : ',';
                                break;
                        }

                        currentProperty = "";
                    }
                }
                else
                {
                    inString = true;
                }

                continue;
            }

            if (inString)
            {
                currentValue.Append(c);
                continue;
            }

            if (c == '[' || c == ']')
                continue;

            if (c == '{')
            {
                code = null;
                name = null;
                nativeName = null;
                defaultLanguage = null;
                defaultCurrency = null;
                dateFormat = null;
                decimalSeparator = null;
                groupSeparator = null;
                continue;
            }

            if (c == '}')
            {
                if (code != null && name != null && nativeName != null &&
                    defaultLanguage != null && defaultCurrency != null &&
                    dateFormat != null && decimalSeparator.HasValue && groupSeparator.HasValue)
                {
                    countries.Add(new CountryModel(
                        code, name, nativeName,
                        defaultLanguage, defaultCurrency,
                        dateFormat, decimalSeparator.Value, groupSeparator.Value));
                }

                continue;
            }

            if (c == ':')
                continue;
            if (c == ',')
                continue;
        }

        return countries.ToImmutable();
    }
}
